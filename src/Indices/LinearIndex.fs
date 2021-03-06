﻿// --------------------------------------------------------------------------------------
// A concrete implementation of an index. Represents an index where the values are sto-
// red in an array (or similar structure) with linearly ordered addresses without holes.
// --------------------------------------------------------------------------------------

namespace FSharp.DataFrame.Indices.Linear

open System
open System.Linq
open System.Collections.Generic
open FSharp.DataFrame
open FSharp.DataFrame.Keys
open FSharp.DataFrame.Addressing
open FSharp.DataFrame.Internal
open FSharp.DataFrame.Indices
open System.Diagnostics

/// An index that maps keys `K` to offsets `Address`. The keys cannot be duplicated.
/// The construction checks if the keys are ordered (using the provided or the default
/// comparer for `K`) and disallows certain operations on unordered indices.
type LinearIndex<'K when 'K : equality> 
  internal (keys:seq<'K>, builder, ?ordered) =

  // Build a lookup table etc.
  let comparer = Comparer<'K>.Default
  let ordered = Lazy.Create(fun () -> 
    // If the caller specified whether the series is ordered, or custom comparer, use that;
    // Otherwise, we do check if it is comparable or nullable (for other types
    // comparer.Compare fails).
    match ordered with
    | Some ord -> ord
    | _ when 
          typeof<IComparable>.IsAssignableFrom(typeof<'K>) ||
          typeof<IComparable<'K>>.IsAssignableFrom(typeof<'K>) -> 
            // This can still fail :-( for example, if the type is a tuple
            // with incomparable values. So, let's just have a fallback returning false...
            try Seq.isSorted keys comparer
            with _ -> false
    | _ when
          typeof<'K>.IsGenericTypeDefinition && typeof<'K>.GetGenericTypeDefinition() = typedefof<Nullable<_>> ->
            try Seq.isSorted keys comparer 
            with _ -> false
    | _ -> false )

  // These are used for NearestSmaller/NearestGreater lookup and 
  // might not work for big data sets...
  let keysArray = lazy Array.ofSeq keys
  let keysArrayRev = lazy (Array.ofSeq keys |> Array.rev)

  let lookup = Dictionary<'K, Address>()
  let addresses = Address.generateRange(Address.rangeOf(keys))
  let mappings = Seq.zip keys addresses
  do for k, v in mappings do 
       match lookup.TryGetValue(k) with
       | true, list -> invalidArg "keys" "Duplicate keys are not allowed in the index."
       | _ -> lookup.[k] <- v  

  /// Exposes keys array for use in the index builder
  member internal index.KeysArray = keysArray

  /// Implements structural equality check against another index
  override index.Equals(another) = 
    match another with
    | null -> false
    | :? IIndex<'K> as another -> Seq.structuralEquals mappings another.Mappings
    | _ -> false

  /// Implement structural hashing against another index
  override index.GetHashCode() =
    mappings |> Seq.structuralHash

  interface IIndex<'K> with
    member x.Keys = keys
    member x.Builder = builder

    /// Perform reverse lookup and return key for an address
    member x.KeyAt(address) =
      match address with 
      | Address.Int i -> keysArray.Value.[i]
      | _ -> invalidOp "This type of index does not support reverse lookup"

    /// Returns whether the specified index is empty
    member x.IsEmpty = keys |> Seq.isEmpty

    /// Returns the range of keys - makes sense only for ordered index
    member x.KeyRange = 
      if not ordered.Value then invalidOp "KeyRange is not supported for unordered index."
      Seq.head keys, Seq.head keysArrayRev.Value

    /// Get the address for the specified key.
    /// The 'semantics' specifies fancy lookup methods.
    member x.Lookup(key, semantics, check) = 
      match lookup.TryGetValue(key), semantics, Address.int32Convertor with

      // When the value exists directly and the user requires exact match, we 
      // just return it (ignoring the fact that Vector value may be missing)
      | (true, res), Lookup.Exact, _ -> OptionalValue((key, res))
      // otherwise, only return it if there is associated value
      | (true, res), _, _ when check res -> OptionalValue((key, res))
      // if we find it, but 'check' does not like it & we're looking for exact, we return missing
      | (true, _), Lookup.Exact, _ -> OptionalValue.Missing

      // If we can convert array index to address, we can use binary search!
      // (Find the index & generate all previous/next indices so that we can 'check' them)
      | _, Lookup.NearestSmaller, Some asAddr when ordered.Value ->
          let addrOpt = Array.binarySearchNearestSmaller key comparer keysArray.Value
          let indices = addrOpt |> Option.map (fun v -> seq { v .. -1 .. 0 })
          let indices = defaultArg indices Seq.empty
          indices 
          |> Seq.filter (asAddr >> check)
          |> Seq.headOrNone
          |> OptionalValue.ofOption
          |> OptionalValue.map (fun idx -> keysArray.Value.[idx], asAddr idx)

      | _, Lookup.NearestGreater, Some asAddr when ordered.Value ->
          let addrOpt = Array.binarySearchNearestGreater key comparer keysArray.Value
          let indices = addrOpt |> Option.map (fun v -> seq { v .. keysArray.Value.Length - 1 })
          let indices = defaultArg indices Seq.empty
          indices 
          |> Seq.filter (asAddr >> check)
          |> Seq.headOrNone
          |> OptionalValue.ofOption
          |> OptionalValue.map (fun idx -> keysArray.Value.[idx], asAddr idx)

      // When we cannot convert array index to address, we have to use sequential search...
      //
      // Find the index of the first key that is greater than the one specified
      // (generate address range and find the address using 'skipWhile')
      | _, Lookup.NearestGreater, None when ordered.Value ->
          Seq.zip keysArray.Value (Address.generateRange(Address.rangeOf(keys)))
          |> Seq.skipWhile (fun (k, _) -> comparer.Compare(k, key) < 0) 
          |> Seq.filter (snd >> check)
          |> Seq.headOrNone
          |> OptionalValue.ofOption

      // Find the index of the last key before the specified one
      // (generate address range prefixed with None, find the first greater key
      // and then return the previous address from the prefixed sequence)
      | _, Lookup.NearestSmaller, None when ordered.Value ->
          let lo, hi = Address.rangeOf(keys)
          Seq.zip keysArrayRev.Value (Address.generateRange(hi, lo))
          |> Seq.skipWhile (fun (k, _) -> comparer.Compare(k, key) > 0) 
          |> Seq.filter (snd >> check)
          |> Seq.headOrNone
          |> OptionalValue.ofOption

      // If we did not find the key (or when we're unsorted & user wants fancy semantics), fail
      | _ -> OptionalValue.Missing

    /// Returns all mappings of the index (key -> address) 
    member x.Mappings = mappings
    /// Returns the range used by the index
    member x.Range = Address.rangeOf(keys)
    /// Are the keys of the index ordered?
    member x.IsOrdered = ordered.Value
    member x.Comparer = comparer


// --------------------------------------------------------------------------------------
// Linear index builder - provides operations for indices (like unioning, 
// intersection, appending and reindexing)
// --------------------------------------------------------------------------------------

/// Index builder object that is associated with `LinearIndex<K>` type. The builder
/// provides operations for manipulating linear indices (and the associated vectors).
type LinearIndexBuilder(vectorBuilder:Vectors.IVectorBuilder) =

  /// Given the result of 'Seq.alignWithOrdering', create a new index
  /// and apply the transformations on two specified vector constructors
  let returnUsingAlignedSequence joined vector1 vector2 ordered : (IIndex<_> * _ * _) = 
    // Create a new index using the sorted keys
    let newIndex = LinearIndex<_>(seq { for k, _, _ in joined -> k}, LinearIndexBuilder.Instance, ?ordered=ordered)
    let range = (newIndex :> IIndex<_>).Range

    // Create relocation transformations for both vectors
    let joinedWithIndex = Seq.zip (Address.generateRange range) joined
    let vect1Reloc = seq { for n, (_, o, _) in joinedWithIndex do if Option.isSome o then yield n, o.Value }
    let newVector1 = Vectors.Relocate(vector1, range, vect1Reloc)
    let vect2Reloc = seq { for n, (_, _, o) in joinedWithIndex do if Option.isSome o then yield n, o.Value }
    let newVector2 = Vectors.Relocate(vector2, range, vect2Reloc)

    // That's it! Return the result.
    ( upcast newIndex, newVector1, newVector2 )

  /// Convert any index to a linear index (and relocate vector accordingly)
  let asLinearIndex (index:IIndex<_>) vector =
    match index with
    | :? LinearIndex<_> as lin -> lin, vector
    | _ ->
      let relocs = index.Mappings |> Seq.mapi (fun i (k, a) -> Address.Int i, a)
      let newVector = Vectors.Relocate(vector, Address.rangeOf(index.Mappings), relocs)
      LinearIndex(index.Mappings |> Seq.map fst, LinearIndexBuilder.Instance), newVector

  /// Instance of the index builder (specialized to Int32 addresses)
  static let indexBuilder = LinearIndexBuilder(Vectors.ArrayVector.ArrayVectorBuilder.Instance)
  /// Provides a global access to an instance of LinearIndexBuilder
  static member Instance = indexBuilder :> IIndexBuilder

  interface IIndexBuilder with
    /// Linear index is always fully evaluated - just return it
    member builder.Project(index) = index

    /// Create an index from the specified data
    member builder.Create<'K when 'K : equality>(keys, ordered) = 
      upcast LinearIndex<'K>(keys, builder, ?ordered=ordered)

    /// Aggregate ordered index
    member builder.Aggregate<'K, 'R, 'TNewKey when 'K : equality and 'TNewKey : equality>
        (index:IIndex<'K>, aggregation, vector, valueSel:_ * _ -> OptionalValue<'R>, keySel:_ * _ -> 'TNewKey) =
      if not index.IsOrdered then 
        invalidOp "Floating window aggregation and chunking is only supported on ordered indices."
      let builder = (builder :> IIndexBuilder)
      let ranges =
        // Get windows based on the key sequence
        let windows = 
          match aggregation with
          | WindowWhile cond -> Seq.windowedWhile cond index.Keys |> Seq.map (fun vs -> DataSegment(Complete, vs))
          | ChunkWhile cond -> Seq.chunkedWhile cond index.Keys |> Seq.map (fun vs -> DataSegment(Complete, vs))
          | WindowSize(size, bounds) -> Seq.windowedWithBounds size bounds index.Keys 
          | ChunkSize(size, bounds) -> Seq.chunkedWithBounds size bounds index.Keys
        // For each window, get a VectorConstruction that represents it
        windows |> Seq.map (fun win -> 
          let index, cmd = 
            builder.GetRange
              ( index, Some(win.Data.[0], BoundaryBehavior.Inclusive), 
                Some(win.Data.[win.Data.Length - 1], BoundaryBehavior.Inclusive), vector)
          win.Kind, (index, cmd) ) |> Array.ofSeq

      /// Build a new index & vector by applying key/value selectors
      let keys = ranges |> Array.map keySel
      let newIndex = builder.Create(keys, None)
      let vect = ranges |> Array.map valueSel |> vectorBuilder.CreateMissing
      newIndex, vect


    /// Group an (un)ordered index
    member builder.GroupBy<'K, 'TNewKey, 'R when 'K : equality and 'TNewKey : equality>
        (index:IIndex<'K>, keySel:'K -> OptionalValue<'TNewKey>, vector, valueSel:_ * _ -> OptionalValue<'R>) =
      let builder = (builder :> IIndexBuilder)
      let ranges =
        // Build a sequence of indices & vector constructions representing the groups
        let windows = index.Keys |> Seq.groupBy keySel |> Seq.choose (fun (k, v) -> 
          if k.HasValue then Some(k.Value, v) else None)
        windows 
        |> Seq.map (fun (key, win) ->
          let relocations = 
            seq { for k, newAddr in Seq.zip win (Address.generateRange(Address.rangeOf(win))) -> 
                    newAddr, index.Lookup(k, Lookup.Exact, fun _ -> true).Value |> snd }
          let newIndex = builder.Create(win, None)
          key, (newIndex, Vectors.Relocate(vector, Address.rangeOf(win), relocations)))
        |> Array.ofSeq

      /// Build a new index & vector by applying value selector
      let keys = ranges |> Seq.map (fun (k, _) -> k)
      let newIndex = builder.Create(keys, None)
      let vect = ranges |> Seq.map valueSel |> Array.ofSeq |> vectorBuilder.CreateMissing
      newIndex, vect

    /// Create chunks based on the specified key sequence
    member builder.Resample<'K, 'TNewKey, 'R when 'K : equality and 'TNewKey : equality> 
        (index:IIndex<'K>, keys:seq<'K>, dir:Direction, vector, valueSel:_ * _ -> OptionalValue<'R>, keySel:_ * _ -> 'TNewKey) =

      if not index.IsOrdered then 
        invalidOp "Resampling is only supported on ordered indices"

      let builder = (builder :> IIndexBuilder)
      let ranges =
        // Build a sequence of indices & vector constructions representing the groups
        let windows = index.Keys |> Seq.chunkedUsing index.Comparer dir keys 
        windows 
        |> Seq.map (fun (key, win) ->
          let relocations = 
            seq { for k, newAddr in Seq.zip win (Address.generateRange(Address.rangeOf(win))) -> 
                    newAddr, index.Lookup(k, Lookup.Exact, fun _ -> true).Value |> snd }
          let newIndex = builder.Create(win, None)
          key, (newIndex, Vectors.Relocate(vector, Address.rangeOf(win), relocations)))
        |> Array.ofSeq

      /// Build a new index & vector by applying value selector
      let keys = ranges |> Array.map (fun (k, sc) -> keySel (k, sc), sc)
      let newIndex = builder.Create(Seq.map fst keys, None)
      let vect = keys |> Seq.map valueSel |> Array.ofSeq |> vectorBuilder.CreateMissing
      newIndex, vect

    /// Order index and build vector transformation 
    member builder.OrderIndex( (index, vector) ) =
      let keys = Array.ofSeq index.Keys
      Array.sortInPlaceWith (fun a b -> index.Comparer.Compare(a, b)) keys
      let newIndex = LinearIndex(keys, builder, true) :> IIndex<_>
      let relocations = 
        seq { for key, oldAddress in index.Mappings ->
                let newAddress = newIndex.Lookup(key, Lookup.Exact, fun _ -> true) 
                if not newAddress.HasValue then failwith "OrderIndex: key not found in the new index"
                snd newAddress.Value, oldAddress }
      newIndex, Vectors.Relocate(vector, newIndex.Range, relocations)


    /// Union the index with another. For sorted indices, this needs to align the keys;
    /// for unordered, it appends new ones to the end.
    member builder.Union<'K when 'K : equality >
        ( (index1:IIndex<'K>, vector1), (index2, vector2) )= 
      let joined, ordered =
        if index1.IsOrdered && index2.IsOrdered then
          try Seq.alignWithOrdering index1.Mappings index2.Mappings index1.Comparer |> Array.ofSeq, Some true
          with :? ComparisonFailedException ->
            Seq.alignWithoutOrdering index1.Mappings index2.Mappings |> Array.ofSeq, None
        else
          Seq.alignWithoutOrdering index1.Mappings index2.Mappings |> Array.ofSeq, None
      returnUsingAlignedSequence joined vector1 vector2 ordered 
        
    /// Append is similar to union, but it also combines the vectors using the specified
    /// vector transformation.
    member builder.Append<'K when 'K : equality >
        ( (index1:IIndex<'K>, vector1), (index2, vector2), transform) = 
      let joined, ordered = 
        if index1.IsOrdered && index2.IsOrdered then
          try Seq.alignWithOrdering index1.Mappings index2.Mappings index1.Comparer |> Array.ofSeq, Some true
          with :? ComparisonFailedException ->
            Seq.alignWithoutOrdering index1.Mappings index2.Mappings |> Array.ofSeq, None
        else
          Seq.alignWithoutOrdering index1.Mappings index2.Mappings |> Array.ofSeq, None
      let newIndex, vec1Cmd, vec2Cmd = returnUsingAlignedSequence joined vector1 vector2 ordered
      newIndex, Vectors.Combine(vec1Cmd, vec2Cmd, transform)

    /// Intersect the index with another. This is the same as
    /// Union, but we filter & only return keys present in both sequences.
    member builder.Intersect<'K when 'K : equality >
        ( (index1:IIndex<'K>, vector1), (index2, vector2) ) = 
      let joined, ordered = 
        if index1.IsOrdered && index2.IsOrdered then
          try Seq.alignWithOrdering index1.Mappings index2.Mappings index1.Comparer |> Array.ofSeq, Some true
          with :? ComparisonFailedException ->
            Seq.alignWithoutOrdering index1.Mappings index2.Mappings |> Array.ofSeq, None
        else
          Seq.alignWithoutOrdering index1.Mappings index2.Mappings |> Array.ofSeq, None
      let joined = joined |> Seq.filter (function _, Some _, Some _ -> true | _ -> false)
      returnUsingAlignedSequence joined vector1 vector2 ordered

    /// Build a new index by getting a key for each old key using the specified function
    member builder.WithIndex<'K, 'TNewKey when 'K : equality  and 'TNewKey : equality>
        (index1:IIndex<'K>, f:Address -> OptionalValue<'TNewKey>, vector) =
      let newKeys =
        [| for key, oldAddress in index1.Mappings do
             let newKey = f oldAddress
             if newKey.HasValue then yield newKey.Value, oldAddress |]
      
      let newIndex = LinearIndex<'TNewKey>(Seq.map fst newKeys, builder)
      let newRange = (newIndex :> IIndex<_>).Range
      let relocations = Seq.zip (Address.generateRange(newRange)) (Seq.map snd newKeys)
      upcast newIndex, Vectors.Relocate(vector, newRange, relocations)


    /// Reorder elements in the index to match with another index ordering
    member builder.Reindex(index1, index2, semantics, vector, condition) = 
      let relocations = seq {  
        for key, newAddress in index2.Mappings do
          let oldAddress = index1.Lookup(key, semantics, condition)
          if oldAddress.HasValue then 
            yield newAddress, oldAddress.Value |> snd }
      Vectors.Relocate(vector, index2.Range, relocations)

    member builder.LookupLevel( (index, vector), searchKey:ICustomLookup<'K> ) =
      let matching = 
        [| for key, addr in index.Mappings do
             if searchKey.Matches(key) then yield addr, key |]
      let range = Address.rangeOf(matching)
      let relocs = Seq.zip (Address.generateRange(range)) (Seq.map fst matching)
      let newIndex = LinearIndex<_>(Seq.map snd matching, builder, index.IsOrdered)
      let newVector = Vectors.Relocate(vector, range, relocs)
      upcast newIndex, newVector

    /// Drop the specified item from the index
    member builder.DropItem<'K when 'K : equality >
        ( (index:IIndex<'K>, vector), key ) = 
      match index.Lookup(key, Lookup.Exact, fun _ -> true) with
      | OptionalValue.Present(addr) ->
          let newVector = Vectors.DropRange(vector, (snd addr, snd addr))
          let newKeys = index.Keys |> Seq.filter ((<>) key)
          let newIndex = LinearIndex<_>(newKeys, builder, index.IsOrdered)
          upcast newIndex, newVector
      | _ ->
          invalidArg "key" (sprintf "The key '%O' is not present in the index." key)


    /// Get a new index representing a sub-index of the current one
    /// (together with a transformation that should be applied to a vector)
    member builder.GetRange<'K when 'K : equality >
        (index:IIndex<'K>, lo, hi, vector) =
      // Default values are specified by the entire range
      let defaults = lazy Address.rangeOf(index.Keys)
      let getBound offs semantics proj = 
        let (|Lookup|_|) x = 
          match index.Lookup(x, semantics, fun _ -> true) with 
          | OptionalValue.Present(_, v) -> Some v | _ -> None
        match offs with 
        | None -> Some(proj defaults.Value, BoundaryBehavior.Inclusive)
        | Some (Lookup i, bound) -> Some(i, bound)
        | _ -> None

      // Create new index using the range & vector transformation
      match getBound lo Lookup.NearestGreater fst, getBound hi Lookup.NearestSmaller snd with
      | Some lo, Some hi ->
          let lo = if snd lo = BoundaryBehavior.Exclusive then Address.increment (fst lo) else fst lo
          let hi = if snd hi = BoundaryBehavior.Exclusive then Address.decrement (fst hi) else fst hi

          let index, vector = asLinearIndex index vector 

          let newKeys = Address.getRange(index.KeysArray.Value, lo, hi) |> Array.ofSeq
          let newVector = Vectors.GetRange(vector, (lo, hi))
          upcast LinearIndex<_>(newKeys, builder, (index :> IIndex<_>).IsOrdered), newVector
      | _ -> upcast LinearIndex<_>([], builder, index.IsOrdered), Vectors.Empty

// --------------------------------------------------------------------------------------
// Functions for creatin linear indices
// --------------------------------------------------------------------------------------

namespace FSharp.DataFrame 

open System.Collections.Generic
open FSharp.DataFrame.Internal
open FSharp.DataFrame.Indices.Linear

/// Defines non-generic `Index` type that provides functions for building indices
/// (hard-bound to `LinearIndexBuilder` type). In F#, the module is automatically opened
/// using `AutoOpen`. The methods are not designed for the use from C#.
[<AutoOpen>]
module FSharpIndexExtensions =
  open System

  /// Type that provides a simple access to creating indices represented
  /// using the built-in `LinearVector` type.
  type Index = 
    /// Create an index from a sequence of keys and check if they are sorted or not
    static member ofKeys<'T when 'T : equality>(keys:seq<'T>) =
      LinearIndexBuilder.Instance.Create<'T>(keys, None)

    /// Create an index from a sequence of keys and assume they are not sorted
    /// (the resulting index is also not sorted).
    static member ofUnorderedKeys<'T when 'T : equality>(keys:seq<'T>) = 
      LinearIndexBuilder.Instance.Create<'T>(keys, Some false)        


/// Type that provides access to creating indices (represented as `LinearIndex` values)
type Index =
  /// Create an index from a sequence of keys and check if they are sorted or not
  [<CompilerMessage("This method is not intended for use from F#.", 10001, IsHidden=true, IsError=false)>]
  static member Create<'T when 'T : equality>(keys:seq<'T>) =
    LinearIndexBuilder.Instance.Create<'T>(keys, None)

  /// Create an index from a sequence of keys and assume they are not sorted
  /// (the resulting index is also not sorted).
  [<CompilerMessage("This method is not intended for use from F#.", 10001, IsHidden=true, IsError=false)>]
  static member CreateUnordered<'T when 'T : equality>(keys:seq<'T>) = 
    LinearIndexBuilder.Instance.Create<'T>(keys, Some false)        
