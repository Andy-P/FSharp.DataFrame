﻿#nowarn "77" // Static constraint in Series.sample requires special + operator

namespace FSharp.DataFrame
open FSharp.DataFrame.Keys

/// Series module comment..
/// 
/// ## Lookup, resampling and scaling
/// More stuff here
///
/// ## Missing values
/// More stuff here
///
/// ## Windowing, chunking and grouping
/// The functions with name starting with `windowed` take a series and generate floating 
/// (overlapping) windows. The `chunk` functions 
///
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Series = 
  open System
  open System.Linq
  open FSharp.DataFrame.Internal
  open FSharp.DataFrame.Vectors
  open MathNet.Numerics.Statistics

  // Non-public helper
  let inline private streamingAggregation f (series:Series<_, _>) =
    match series.Vector.Data with
    | VectorData.DenseList list -> f (list :> seq<_>)
    | VectorData.SparseList list -> f (Seq.choose OptionalValue.asOption list)
    | VectorData.Sequence seq -> f (Seq.choose OptionalValue.asOption seq)

  let inline private fastAggregation flist foptlist fseq (series:Series<_, _>) =
    match series.Vector.Data with
    | VectorData.DenseList list -> flist list
    | VectorData.SparseList list -> foptlist list
    | VectorData.Sequence seq -> fseq (Seq.choose OptionalValue.asOption seq)

  /// [omit]
  /// Does stuff
  let inline fastStatBy keySelector flist foptlist fseq (series:Series<_, _>) : Series<_, _> = 
    series.GroupBy
      ( (fun key ser -> keySelector key),
        (fun key ser -> OptionalValue(fastAggregation flist foptlist fseq ser)))

  [<CompiledName("Statistic")>]
  let inline stat op (series:Series<'K, _>) = 
    series |> streamingAggregation op

  [<CompiledName("Sum")>]
  let inline sum (series:Series<_, _>) = 
    series |> fastAggregation ReadOnlyCollection.sum ReadOnlyCollection.sumOptional Seq.sum

  [<CompiledName("Mean")>]
  let inline mean (series:Series<_, _>) = 
    series |> fastAggregation ReadOnlyCollection.average ReadOnlyCollection.averageOptional Seq.average

  [<CompiledName("StandardDeviation")>]
  let inline sdv (series:Series<'K, float>) = series |> stat Statistics.StandardDeviation 

  [<CompiledName("Median")>]
  let inline median (series:Series<'K, float>) = series |> stat Statistics.Median

  let reduce op (series:Series<'K, 'T>) = 
    series |> fastAggregation (ReadOnlyCollection.reduce op) (ReadOnlyCollection.reduceOptional op) (Seq.reduce op)


  [<CompiledName("StatisticBy")>]
  let inline statBy keySelector op (series:Series<_, _>) : Series<_, _> = 
    series.GroupBy
      ( (fun key ser -> keySelector key),
        (fun key ser -> OptionalValue(stat op ser)))

  [<CompiledName("SumBy")>]
  let inline sumBy keySelector (series:Series<_, _>) = 
    series |> fastStatBy keySelector ReadOnlyCollection.sum ReadOnlyCollection.sumOptional Seq.sum

  [<CompiledName("CountBy")>]
  let inline countBy keySelector (series:Series<_, _>) = 
    series |> fastStatBy keySelector ReadOnlyCollection.length ReadOnlyCollection.lengthOptional Seq.length

  [<CompiledName("MeanBy")>]
  let inline meanBy keySelector (series:Series<_, _>) = 
    series |> fastStatBy keySelector ReadOnlyCollection.average ReadOnlyCollection.averageOptional Seq.average

  [<CompiledName("StandardDeviationBy")>]
  let inline sdvBy keySelector (series:Series<_, float>) = 
    series |> statBy keySelector Statistics.StandardDeviation 

  [<CompiledName("MedianBy")>]
  let inline medianBy keySelector (series:Series<_, float>) = 
    series |> statBy keySelector Statistics.Median


  let reduceBy keySelector op (series:Series<_, _>) = 
    series.GroupBy
      ( (fun key ser -> keySelector key),
        (fun key ser -> OptionalValue(op ser)))
    

  /// Return observations with available values. The operation skips over 
  /// all keys with missing values (such as values created from `null`,
  /// `Double.NaN`, or those that are missing due to outer join etc.).
  let observations (series:Series<'K, 'T>) = seq { 
    for key, address in series.Index.Mappings do
      let v = series.Vector.GetValue(address)
      if v.HasValue then yield key, v.Value }
  
  /// Returns all keys from the sequence, together with the associated (optional) values. 
  let observationsAll (series:Series<'K, 'T>) = seq { 
    for key, address in series.Index.Mappings ->
      key, OptionalValue.asOption (series.Vector.GetValue(address)) }

  /// Create a new series that contains values for all provided keys.
  /// Use the specified lookup semantics - for exact matching, use `getAll`
  let lookupAll keys lookup (series:Series<'K, 'T>) = series.GetItems(keys, lookup)

  /// Create a new series that contains values for all provided keys.
  /// Uses exact lookup semantics for key lookup - use `lookupAll` for more options
  let getAll keys (series:Series<'K, 'T>) = series.GetItems(keys)

  /// Get the value for the specified key.
  /// Use the specified lookup semantics - for exact matching, use `get`
  let lookup key lookup (series:Series<'K, 'T>) = series.Get(key, lookup)

  /// Get the value for the specified key.
  /// Uses exact lookup semantics for key lookup - use `lookupAll` for more options
  let get key (series:Series<'K, 'T>) = series.Get(key)

  let tryLookup key lookup (series:Series<'K, 'T>) = series.TryGet(key, lookup) |> OptionalValue.asOption

  let tryGet key (series:Series<'K, 'T>) = series.TryGet(key) |> OptionalValue.asOption

  let tryGetAt index (series:Series<'K, 'T>) = series.TryGetAt(index) |> OptionalValue.asOption
  let getAt index (series:Series<'K, 'T>) = series.GetAt(index)

  let realign keys (series:Series<'K, 'T>) = 
    series.Realign(keys)

  let indexOrdinally (series:Series<'K, 'T>) = 
    series.IndexOrdinally()

  let indexWith (keys:seq<'K2>) (series:Series<'K1, 'T>) = 
    series.IndexWith(keys)

  let filter f (series:Series<'K, 'T>) = 
    series.Where(Func<_, _>(fun (KeyValue(k,v)) -> f k v))

  let filterValues f (series:Series<'K, 'T>) = 
    series.Where(Func<_, _>(fun (KeyValue(k,v)) -> f v))

  let map (f:'K -> 'T -> 'R) (series:Series<'K, 'T>) = 
    series.Select(fun (KeyValue(k,v)) -> f k v)

  let mapValues (f:'T -> 'R) (series:Series<'K, 'T>) = 
    series.Select(fun (KeyValue(k,v)) -> f v)

  let mapKeys (f:'K -> 'R) (series:Series<'K, 'T>) = 
    series.SelectKeys(fun kvp -> f kvp.Key)

  let filterAll f (series:Series<'K, 'T>) = 
    series.WhereOptional(fun kvp -> f kvp.Key (OptionalValue.asOption kvp.Value))

  let mapAll (f:_ -> _ -> option<'R>) (series:Series<'K, 'T>) = 
    series.SelectOptional(fun kvp -> 
      f kvp.Key (OptionalValue.asOption kvp.Value) |> OptionalValue.ofOption)

  /// `result[k] = series[k] - series[k - offset]`
  let inline diff offset (series:Series<'K, 'T>) = 
    series.Aggregate
      ( WindowSize((abs offset) + 1, Boundary.Skip), 
        (fun ks -> if offset < 0 then ks.Data.Keys.First() else ks.Data.Keys.Last() ),
        (fun ds ->  
          let h, t = ds.Data.Values.First(), ds.Data.Values.Last() in 
          if offset < 0 then h - t else t - h) )


  // Counting & checking if values are present

  /// Returns the total number of values in the specified series. This excludes
  /// missing values or not available values (such as values created from `null`,
  /// `Double.NaN`, or those that are missing due to outer join etc.).
  let countValues (series:Series<'K, 'T>) = series.Values |> Seq.length

  /// Returns the total number of keys in the specified series. This returns
  /// the total length of the series, including keys for which there is no 
  /// value available.
  let countKeys (series:Series<'K, 'T>) = series.Keys |> Seq.length

  let hasAll keys (series:Series<'K, 'T>) = 
    keys |> Seq.forall (fun k -> series.TryGet(k).HasValue)
  let hasSome keys (series:Series<'K, 'T>) = 
    keys |> Seq.exists (fun k -> series.TryGet(k).HasValue)
  let hasNone keys (series:Series<'K, 'T>) = 
    keys |> Seq.forall (fun k -> series.TryGet(k).HasValue |> not)
  let has key (series:Series<'K, 'T>) = series.TryGet(key).HasValue
  let hasNot key (series:Series<'K, 'T>) = series.TryGet(key).HasValue

  let lastKey (series:Series< 'K , 'V >) = series.KeyRange |> snd
  let firstKey (series:Series< 'K , 'V >) = series.KeyRange |> fst
  let lastValue (series:Series< 'K , 'V >) = series |> get (series.KeyRange |> snd)
  let firstValue (series:Series< 'K , 'V >) = series |> get (series.KeyRange |> fst)

  let values (series:Series<'K, 'T>) = series.Values
  let keys (series:Series<'K, 'T>) = series.Keys

  let shift offset (series:Series<'K, 'T>) = 
    let win = WindowSize((abs offset) + 1, Boundary.Skip)
    let shifted = 
      if offset < 0 then
        let offset = -offset
        series.Aggregate
          ( win, (fun s -> s.Data.Keys.First()),
            (fun s -> s.Data.Values |> Seq.nth offset) ) 
      else
        series.Aggregate
          ( win, (fun s -> s.Data.Keys.Last()),
            (fun s -> s.Data.Values |> Seq.head) )
    shifted //.GetItems(series.Keys)

  let takeLast count (series:Series<'K, 'T>) = 
    let keys = series.Keys |> Seq.lastFew count 
    Series(keys, seq { for k in keys -> series.[k] })

  let inline maxBy f (series:Series<'K, 'T>) = 
    series |> observations |> Seq.maxBy (snd >> f)

  let inline minBy f (series:Series<'K, 'T>) = 
    series |> observations |> Seq.maxBy (snd >> f)

  // ----------------------------------------------------------------------------------------------
  // Windowing, chunking and grouping
  // ----------------------------------------------------------------------------------------------

  /// Aggregates an ordered series using the method specified by `Aggregation<K>` and 
  /// returns the windows or chunks as nested series. A key for each window or chunk is
  /// selected using the specified `keySelector`.
  ///
  /// ## Parameters
  ///  - `aggregation` - Specifies the aggregation method using `Aggregation<K>`. This is
  ///    a discriminated union listing various chunking and windowing conditions.
  ///  - `keySelector` - A function that is called on each chunk to obtain a key.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let aggregate aggregation keySelector (series:Series<'K, 'T>) : Series<'TNewKey, _> =
    series.Aggregate
      ( aggregation, System.Func<_, _>(keySelector), System.Func<_, _>(id))

  /// Aggregates an ordered series using the method specified by `Aggregation<K>` and then
  /// applies the provided value selector `f` on each window or chunk to produce the result
  /// which is returned as a new series. A key for each window or chunk is
  /// selected using the specified `keySelector`.
  ///
  /// ## Parameters
  ///  - `aggregation` - Specifies the aggregation method using `Aggregation<K>`. This is
  ///    a discriminated union listing various chunking and windowing conditions.
  ///  - `keySelector` - A function that is called on each chunk to obtain a key.
  ///  - `f` - A value selector function that is called to aggregate each chunk or window.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let aggregateInto aggregation keySelector f (series:Series<'K, 'T>) : Series<'TNewKey, 'R> =
    series.Aggregate
      ( aggregation, System.Func<_, _>(keySelector), System.Func<_, _>(f))

  /// Creates a sliding window using the specified size and boundary behavior and then
  /// applies the provided value selector `f` on each window to produce the result
  /// which is returned as a new series. The key is the last key of the window, unless
  /// boundary behavior is `Boundary.AtEnding` (in which case it is the first key).
  ///
  /// ## Parameters
  ///  - `bounds` - Specifies the window size and bounary behavior. The boundary behavior
  ///    can be `Boundary.Skip` (meaning that no incomplete windows are produced), 
  ///    `Boundary.AtBeginning` (meaning that incomplete windows are produced at the beginning)
  ///    or `Boundary.AtEnding` (to produce incomplete windows at the end of series)
  ///  - `f` - A value selector that is called to aggregate each window.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let windowSizeInto bounds f (series:Series<'K, 'T>) : Series<'K, 'R> =
    let dir = if snd bounds = Boundary.AtEnding then Direction.Forward else Direction.Backward
    let keySel = System.Func<DataSegment<Series<_, _>>, _>(fun data -> 
      if dir = Direction.Backward then data.Data.Index.Keys |> Seq.last
      else data.Data.Index.Keys |> Seq.head )
    series.Aggregate(WindowSize(bounds), keySel, (fun ds -> f ds))

  /// Creates a sliding window using the specified size and boundary behavior and returns
  /// the produced windows as a nested series. The key is the last key of the window, unless
  /// boundary behavior is `Boundary.AtEnding` (in which case it is the first key).
  ///
  /// ## Parameters
  ///  - `bounds` - Specifies the window size and bounary behavior. The boundary behavior
  ///    can be `Boundary.Skip` (meaning that no incomplete windows are produced), 
  ///    `Boundary.AtBeginning` (meaning that incomplete windows are produced at the beginning)
  ///    or `Boundary.AtEnding` (to produce incomplete windows at the end of series)
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline windowSize bounds (series:Series<'K, 'T>) = 
    windowSizeInto bounds DataSegment.data series 

  // Based on distance

  /// Creates a sliding window based on distance between keys. A window is started at each
  /// input element and ends once the distance between the first and the last key is greater
  /// than the specified `distance`. Each window is then aggregated into a value using the
  /// specified function `f`. The key of each window is the key of the first element in the window.
  ///
  /// ## Parameters
  ///  - `distance` - The maximal allowed distance between keys of a window. Note that this
  ///    is an inline function - there must be `-` operator defined between `distance` and the
  ///    keys of the series.
  ///  - `f` - A function that is used to aggregate each window into a single value.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline windowDistInto distance f (series:Series<'K, 'T>) =
    series.Aggregate(WindowWhile(fun skey ekey -> (ekey - skey) < distance), (fun d -> d.Data.Keys |> Seq.head), fun ds -> f ds.Data)

  /// Creates a sliding window based on distance between keys. A window is started at each
  /// input element and ends once the distance between the first and the last key is greater
  /// than the specified `distance`. The windows are then returned as a nested series.
  /// The key of each window is the key of the first element in the window.
  ///
  /// ## Parameters
  ///  - `distance` - The maximal allowed distance between keys of a window. Note that this
  ///    is an inline function - there must be `-` operator defined between `distance` and the
  ///    keys of the series.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline windowDist (distance:'D) (series:Series<'K, 'T>) = 
    windowDistInto distance id series 

  // Window using while

  /// Creates a sliding window based on a condition on keys. A window is started at each
  /// input element and ends once the specified `cond` function returns `false` when called on 
  /// the first and the last key of the window. Each window is then aggregated into a value using the
  /// specified function `f`. The key of each window is the key of the first element in the window.
  ///
  /// ## Parameters
  ///  - `cond` - A function that is called on the first and the last key of a window
  ///    to determine when a window should end.
  ///  - `f` - A function that is used to aggregate each window into a single value.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline windowWhileInto cond f (series:Series<'K, 'T>) =
    series.Aggregate(WindowWhile(cond), (fun d -> d.Data.Keys |> Seq.head), fun ds -> f ds.Data)

  /// Creates a sliding window based on a condition on keys. A window is started at each
  /// input element and ends once the specified `cond` function returns `false` when called on 
  /// the first and the last key of the window. The windows are then returned as a nested series.
  /// The key of each window is the key of the first element in the window.
  ///
  /// ## Parameters
  ///  - `cond` - A function that is called on the first and the last key of a window
  ///    to determine when a window should end.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline windowWhile cond (series:Series<'K, 'T>) = 
    windowWhileInto cond id series 

  // Chunk based on size

  /// Aggregates the input into a series of adacent chunks using the specified size and boundary behavior and then
  /// applies the provided value selector `f` on each chunk to produce the result
  /// which is returned as a new series. The key is the last key of the chunk, unless
  /// boundary behavior is `Boundary.AtEnding` (in which case it is the first key).
  ///
  /// ## Parameters
  ///  - `bounds` - Specifies the chunk size and bounary behavior. The boundary behavior
  ///    can be `Boundary.Skip` (meaning that no incomplete chunks are produced), 
  ///    `Boundary.AtBeginning` (meaning that incomplete chunks are produced at the beginning)
  ///    or `Boundary.AtEnding` (to produce incomplete chunks at the end of series)
  ///  - `f` - A value selector that is called to aggregate each chunk.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunkSizeInto bounds f (series:Series<'K, 'T>) : Series<'K, 'R> =
    series.Aggregate(ChunkSize(bounds), (fun d -> d.Data.Keys |> Seq.head), fun ds -> f ds)

  /// Aggregates the input into a series of adacent chunks using the specified size and boundary behavior and returns
  /// the produced chunks as a nested series. The key is the last key of the chunk, unless
  /// boundary behavior is `Boundary.AtEnding` (in which case it is the first key).
  ///
  /// ## Parameters
  ///  - `bounds` - Specifies the chunk size and bounary behavior. The boundary behavior
  ///    can be `Boundary.Skip` (meaning that no incomplete chunks are produced), 
  ///    `Boundary.AtBeginning` (meaning that incomplete chunks are produced at the beginning)
  ///    or `Boundary.AtEnding` (to produce incomplete chunks at the end of series)
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunkSize bounds (series:Series<'K, 'T>) = 
    chunkSizeInto bounds DataSegment.data series 

  // Chunk based on distance

  /// Aggregates the input into a series of adacent chunks. A chunk is started once
  /// the distance between the first and the last key of a previous chunk is greater
  /// than the specified `distance`. Each chunk is then aggregated into a value using the
  /// specified function `f`. The key of each chunk is the key of the first element in the chunk.
  ///
  /// ## Parameters
  ///  - `distance` - The maximal allowed distance between keys of a chunk. Note that this
  ///    is an inline function - there must be `-` operator defined between `distance` and the
  ///    keys of the series.
  ///  - `f` - A value selector that is called to aggregate each chunk.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunkDistInto (distance:^D) f (series:Series<'K, 'T>) : Series<'K, 'R> =
    series.Aggregate(ChunkWhile(fun skey ekey -> (ekey - skey) < distance), (fun d -> d.Data.Keys |> Seq.head), fun ds -> f ds.Data)

  /// Aggregates the input into a series of adacent chunks. A chunk is started once
  /// the distance between the first and the last key of a previous chunk is greater
  /// than the specified `distance`. The chunks are then returned as a nested series.
  /// The key of each chunk is the key of the first element in the chunk.
  ///
  /// ## Parameters
  ///  - `distance` - The maximal allowed distance between keys of a chunk. Note that this
  ///    is an inline function - there must be `-` operator defined between `distance` and the
  ///    keys of the series.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunkDist (distance:^D) (series:Series<'K, 'T>) = 
    chunkDistInto distance id series 

  // Chunk while

  /// Aggregates the input into a series of adacent chunks based on a condition on keys. A chunk is started 
  /// once the specified `cond` function returns `false` when called on  the first and the last key of the 
  /// previous chunk. Each chunk is then aggregated into a value using the
  /// specified function `f`. The key of each chunk is the key of the first element in the chunk.
  ///
  /// ## Parameters
  ///  - `cond` - A function that is called on the first and the last key of a chunk
  ///    to determine when a window should end.
  ///  - `f` - A value selector that is called to aggregate each chunk.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunkWhileInto cond f (series:Series<'K, 'T>) =
    series.Aggregate(ChunkWhile(cond), (fun d -> d.Data.Keys |> Seq.head), fun ds -> f ds.Data)

  /// Aggregates the input into a series of adacent chunks based on a condition on keys. A chunk is started 
  /// once the specified `cond` function returns `false` when called on  the first and the last key of the 
  /// previous chunk. The chunks are then returned as a nested series.
  /// The key of each chunk is the key of the first element in the chunk.
  ///
  /// ## Parameters
  ///  - `cond` - A function that is called on the first and the last key of a chunk
  ///    to determine when a window should end.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunkWhile cond (series:Series<'K, 'T>) = 
    chunkWhileInto cond id series 

  // Most common-case functions

  /// Creates a sliding window using the specified size and then applies the provided 
  /// value selector `f` on each window to produce the result which is returned as a new series. 
  /// This function skips incomplete chunks - you can use `Series.windowSizeInto` for more options.
  ///
  /// ## Parameters
  ///  - `size` - The size of the sliding window.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline windowInto size f (series:Series<'K, 'T>) : Series<'K, 'R> =
    windowSizeInto (size, Boundary.Skip) (DataSegment.data >> f) series

  /// Creates a sliding window using the specified size and returns the produced windows as 
  /// a nested series. The key in the new series is the last key of the window. This function
  /// skips incomplete chunks - you can use `Series.windowSize` for more options.
  ///
  /// ## Parameters
  ///  - `size` - The size of the sliding window.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline window size (series:Series<'K, 'T>) =
    windowSize (size, Boundary.Skip) series

  /// Aggregates the input into a series of adacent chunks and then applies the provided 
  /// value selector `f` on each chunk to produce the result which is returned as a new series. 
  /// The key in the new series is the last key of the chunk. This function
  /// skips incomplete chunks - you can use `Series.chunkSizeInto` for more options.
  ///
  /// ## Parameters
  ///  - `size` - The size of the chunk.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunkInto size f (series:Series<'K, 'T>) : Series<'K, 'R> =
    chunkSizeInto (size, Boundary.Skip) (DataSegment.data >> f) series

  /// Aggregates the input into a series of adacent chunks and returns the produced chunks as
  /// a nested series. The key in the new series is the last key of the chunk. This function
  /// skips incomplete chunks - you can use `Series.chunkSize` for more options.
  ///
  /// ## Parameters
  ///  - `size` - The size of the chunk.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let inline chunk size (series:Series<'K, 'T>) =
    chunkSize (size, Boundary.Skip) series

  // Pairwise

  /// Returns a series containing the predecessor and an element for each input, except
  /// for the first one. The returned series is one key shorter (it does not contain a 
  /// value for the first key).
  ///
  /// ## Parameters
  ///  - `series` - The input series to be aggregated.
  ///
  /// ## Example
  ///
  ///     let input = series [ 1 => 'a'; 2 => 'b'; 3 => 'c']
  ///     let res = input |> Series.pairwise
  ///     res = series [2 => ('a', 'b'); 3 => ('b', 'c') ]
  ///
  /// [category:Windowing, chunking and grouping]
  let pairwise (series:Series<'K, 'T>) = 
    series.Pairwise() |> map (fun k v -> v.Data)

  /// Aggregates the input into pairs containing the predecessor and an element for each input, except
  /// for the first one. Then calls the specified aggregation function `f` with a tuple and a key.
  /// The returned series is one key shorter (it does not contain a  value for the first key).
  ///
  /// ## Parameters
  ///  - `f` - A function that is called for each pair to produce result in the final series.
  ///  - `series` - The input series to be aggregated.
  ///
  /// [category:Windowing, chunking and grouping]
  let pairwiseWith f (series:Series<'K, 'T>) = 
    series.Pairwise() |> map (fun k v -> f k v.Data)

  // Grouping

  /// Groups a series (ordered or unordered) using the specified key selector (`keySelector`) 
  /// and then aggregates each group into a single value, returned in the resulting series,
  /// using the provided `valueSelector` function.
  ///
  /// ## Parameters
  ///  - `keySelector` - Generates a new key that is used for aggregation, based on the original 
  ///    key and value. The new key must support equality testing.
  ///  - `valueSelector` - A value selector function that is called to aggregate 
  ///    each group of collected elements.
  ///  - `series` - An input series to be grouped. 
  ///
  /// [category:Windowing, chunking and grouping]
  let groupInto (keySelector:'K -> 'T -> 'TNewKey) f (series:Series<'K, 'T>) : Series<'TNewKey, 'TNewValue> =
    series.GroupBy(keySelector, fun k s -> OptionalValue(f k s))

  /// Groups a series (ordered or unordered) using the specified key selector (`keySelector`) 
  /// and then returns a series of (nested) series as the result. The outer series is indexed by
  /// the newly produced keys, the nested series are indexed with the original keys.
  ///
  /// ## Parameters
  ///  - `keySelector` - Generates a new key that is used for aggregation, based on the original 
  ///    key and value. The new key must support equality testing.
  ///  - `series` - An input series to be grouped. 
  ///
  /// [category:Windowing, chunking and grouping]
  let groupBy (keySelector:'K -> 'T -> 'TNewKey) (series:Series<'K, 'T>) =
    groupInto keySelector (fun k s -> s) series

  // ----------------------------------------------------------------------------------------------
  // Handling of missing values
  // ----------------------------------------------------------------------------------------------

  /// Drop missing values from the specified series. The returned series contains 
  /// only those keys for which there is a value available in the original one.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be filtered
  ///
  /// ## Example
  ///
  ///     let s = series [ 1 => 1.0; 2 => Double.NaN ]
  ///     s |> Series.dropMissing 
  ///     [fsi:val it : Series<int,float> = series [ 1 => 1]
  ///
  /// [category:Missing values]
  let dropMissing (series:Series<'K, 'T>) = 
    series.WhereOptional(fun (KeyValue(k, v)) -> v.HasValue)

  /// Fill missing values in the series using the specified function.
  /// The specified function is called with all keys for which the series
  /// does not contain value and the result of the call is used in place 
  /// of the missing value. 
  ///
  /// ## Parameters
  ///  - `series` - An input series that is to be filled
  ///  - `f` - A function that takes key `K` and generates a value to be
  ///    used in a place where the original series contains a missing value.
  ///
  /// ## Remarks
  /// This function can be used to implement more complex interpolation.
  /// For example see [handling missing values in the tutorial](../features.html#missing)
  ///
  /// [category:Missing values]
  let fillMissingUsing f (series:Series<'K, 'T>) = 
    series |> mapAll (fun k -> function 
      | None -> Some(f k)
      | value -> value)

  /// Fill missing values in the series with a constant value.
  ///
  /// ## Parameters
  ///  - `series` - An input series that is to be filled
  ///  - `value` - A constant value that is used to fill all missing values
  ///
  /// [category:Missing values]
  let fillMissingWith value (series:Series<'K, 'T>) = 
    let fillCmd = Vectors.FillMissing(Vectors.Return 0, VectorFillMissing.Constant value)
    let newVector = series.VectorBuilder.Build(fillCmd, [|series.Vector|])
    Series<_, _>(series.Index, newVector, series.VectorBuilder, series.IndexBuilder)

  /// Fill missing values in the series with the nearest available value
  /// (using the specified direction). Note that the series may still contain
  /// missing values after call to this function. This operation can only be
  /// used on ordered series.
  ///
  /// ## Parameters
  ///  - `series` - An input series that is to be filled
  ///  - `direction` - Specifies the direction used when searching for 
  ///    the nearest available value. `Backward` means that we want to
  ///    look for the first value with a smaller key while `Forward` searches
  ///    for the nearest greater key.
  ///
  /// ## Example
  ///
  ///     let sample = Series.ofValues [ Double.NaN; 1.0; Double.NaN; 3.0 ]
  ///
  ///     // Returns a series consisting of [1; 1; 3; 3]
  ///     sample |> Series.fillMissing Direction.Backward
  ///
  ///     // Returns a series consisting of [<missing>; 1; 1; 3]
  ///     sample |> Series.fillMissing Direction.Forward 
  ///
  /// [category:Missing values]
  let fillMissing direction (series:Series<'K, 'T>) = 
    let fillCmd = Vectors.FillMissing(Vectors.Return 0, VectorFillMissing.Direction direction)
    let newVector = series.VectorBuilder.Build(fillCmd, [|series.Vector|])
    Series<_, _>(series.Index, newVector, series.VectorBuilder, series.IndexBuilder)

  // ----------------------------------------------------------------------------------------------
  // Resampling and similar stuff
  // ----------------------------------------------------------------------------------------------

  /// Resample the series based on a provided collection of keys. The values of the series
  /// are aggregated into chunks based on the specified keys. Depending on `direction`, the 
  /// specified key is either used as the smallest or as the greatest key of the chunk (with
  /// the exception of boundaries that are added to the first/last chunk).
  /// Such chunks are then aggregated using the provided function `f`.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `keys` - A collection of keys to be used for resampling of the series
  ///  - `dir` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///  - `f` - A function that is used to collapse a generated chunk into a 
  ///    single value. Note that this function may be called with empty series.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered.
  ///
  /// [category:Lookup, resampling and scaling]
  let resampleInto keys dir f (series:Series<'K, 'V>) =
    series.Resample(keys, dir, (fun k s -> f k s))

  /// Resample the series based on a provided collection of keys. The values of the series
  /// are aggregated into chunks based on the specified keys. Depending on `direction`, the 
  /// specified key is either used as the smallest or as the greatest key of the chunk (with
  /// the exception of boundaries that are added to the first/last chunk).
  /// Such chunks are then returned as nested series.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `keys` - A collection of keys to be used for resampling of the series
  ///  - `dir` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered.
  ///
  /// [category:Lookup, resampling and scaling]
  let resample keys dir (series:Series<'K, 'V>) =
    resampleInto keys dir (fun k s -> s) series

  /// Resample the series based on equivalence class on the keys. A specified function
  /// `keyProj` is used to project keys to another space and the observations for which the 
  /// projected keys are equivalent are grouped into chunks. The chunks are then transformed
  /// to values using the provided function `f`.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `keyProj` - A function that transforms keys from original space to a new 
  ///    space (which is then used for grouping based on equivalence)
  ///  - `f` - A function that is used to collapse a generated chunk into a 
  ///    single value. 
  ///
  /// ## Remarks
  /// This function is similar to `Series.chunkBy`, with the exception that it transforms
  /// keys to a new space.
  ///
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. For unordered
  /// series, similar functionality can be implemented using `Series.groupBy`.
  ///
  /// [category:Lookup, resampling and scaling]
  let inline resampleEquivInto (keyProj:'K1 -> 'K2) (f:_ -> 'V2) (series:Series<'K1, 'V1>) =
    series 
    |> chunkWhile (fun k1 k2 -> keyProj k1 = keyProj k2)
    |> mapKeys keyProj
    |> mapValues f

  /// Resample the series based on equivalence class on the keys. A specified function
  /// `keyProj` is used to project keys to another space and the observations for which the 
  /// projected keys are equivalent are grouped into chunks. The chunks are then returned
  /// as nested series.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `keyProj` - A function that transforms keys from original space to a new 
  ///    space (which is then used for grouping based on equivalence)
  ///
  /// ## Remarks
  /// This function is similar to `Series.chunkBy`, with the exception that it transforms
  /// keys to a new space.
  ///
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. For unordered
  /// series, similar functionality can be implemented using `Series.groupBy`.
  ///
  /// [category:Lookup, resampling and scaling]
  let inline resampleEquiv (keyProj:'K1 -> 'K2) (series:Series<'K1, 'V1>) =
    resampleEquivInto keyProj id series

  /// Resample the series based on equivalence class on the keys and also generate values 
  /// for all keys of the target space that are between the minimal and maximal key of the
  /// specified series (e.g. generate value for all days in the range covered by the series).
  /// A specified function `keyProj` is used to project keys to another space and `nextKey`
  /// is used to generate all keys in the range. The chunk is then aggregated using `f`.
  ///
  /// When there are no values for a (generated) key, then the function behaves according to
  /// `fillMode`. It can look at the greatest value of previous chunk or smallest value of the
  /// next chunk, or it produces an empty series.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `fillMode` - When set to `Lookup.NearestSmaller` or `Lookup.NearestGreater`, 
  ///     the function searches for a nearest available observation in an neighboring chunk.
  ///     Otherwise, the function `f` is called with an empty series as an argument.
  ///  - `keyProj` - A function that transforms keys from original space to a new 
  ///    space (which is then used for grouping based on equivalence)
  ///  - `nextKey` - A function that gets the next key in the transformed space
  ///  - `f` - A function that is used to collapse a generated chunk into a 
  ///    single value. The function may be called on empty series when `fillMode` is
  ///    `Lookup.Exact`.
  ///    
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  ///
  /// [category:Lookup, resampling and scaling]
  let resampleUniformInto (fillMode:Lookup) (keyProj:'K1 -> 'K2) (nextKey:'K2 -> 'K2) f (series:Series<'K1, 'V>) =
    let min, max = series.KeyRange
    
    // Generate keys of the new space that fit in the range of the series
    let min, max = keyProj min, keyProj max
    let keys = min |> Seq.unfold (fun dt -> 
      if dt <= max then Some(dt, nextKey dt) else None) 
    
    // Chunk existing values and align them to the new keys
    let reindexed = 
      series 
      |> chunkWhile (fun k1 k2 -> keyProj k1 = keyProj k2)
      |> mapKeys keyProj
      |> realign keys

    // For keys that have no values, use empty series or singleton series from above/below
    reindexed
    |> fillMissingUsing (fun k ->
        match fillMode with
        | Lookup.NearestSmaller ->
            let res = reindexed.Get(k, fillMode)
            Series([res.KeyRange |> snd], [res.[res.KeyRange |> snd]])
        | Lookup.NearestGreater ->
            let res = reindexed.Get(k, fillMode)
            Series([res.KeyRange |> fst], [res.[res.KeyRange |> fst]]) 
        | _ -> Series([], [])  )
    |> mapValues f

  /// Resample the series based on equivalence class on the keys and also generate values 
  /// for all keys of the target space that are between the minimal and maximal key of the
  /// specified series (e.g. generate value for all days in the range covered by the series).
  /// A specified function `keyProj` is used to project keys to another space and `nextKey`
  /// is used to generate all keys in the range. Then return the chunks as nested series.
  ///
  /// When there are no values for a (generated) key, then the function behaves according to
  /// `fillMode`. It can look at the greatest value of previous chunk or smallest value of the
  /// next chunk, or it produces an empty series.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `fillMode` - When set to `Lookup.NearestSmaller` or `Lookup.NearestGreater`, 
  ///     the function searches for a nearest available observation in an neighboring chunk.
  ///     Otherwise, the function `f` is called with an empty series as an argument.
  ///  - `keyProj` - A function that transforms keys from original space to a new 
  ///    space (which is then used for grouping based on equivalence)
  ///  - `nextKey` - A function that gets the next key in the transformed space
  ///    
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  ///
  /// [category:Lookup, resampling and scaling]
  let resampleUniform fillMode (keyProj:'K1 -> 'K2) (nextKey:'K2 -> 'K2) (series:Series<'K1, 'V>) =
    resampleUniformInto fillMode keyProj nextKey id series

  /// [omit]
  /// Module that contains an implementation of sampling for `sampleTime` and 
  /// `sampleTimeInto`. For technical reasons (`inline`) this is public..
  module Implementation = 

    let generateKeys add startOpt interval dir (series:Series<'K, 'V>) =
      let smallest, largest = series.KeyRange
      let comparer = System.Collections.Generic.Comparer<'K>.Default
      let rec genKeys current = seq {
        // For Backward, we need one more key after the end
        if dir = Direction.Backward then yield current
        if comparer.Compare(current, largest) <= 0 then
          // For Forward, we only want keys in the range
          if dir = Direction.Forward then yield current
          yield! genKeys (add current interval) }
      genKeys (defaultArg startOpt smallest)

    /// Given a specified starting time and time span, generates all keys that fit in the
    /// range of the series (and one additional, if `dir = Backward`) and then performs
    /// sampling using `resampleInto`.
    let sampleTimeIntoInternal add startOpt (interval:'T) dir f (series:Series<'K , 'V>) =
      resampleInto (generateKeys add startOpt interval dir series) dir f series

    /// Given a specified starting time and time span, generates all keys that fit in the
    /// range of the series (and one additional, if `dir = Backward`) and then performs lookup
    /// for each key using the specified direction
    let lookupTimeInternal add startOpt (interval:'T) dir lookup (series:Series<'K , 'V>) =
      lookupAll (generateKeys add startOpt interval dir series) lookup series

  /// Performs sampling by time and aggregates chunks obtained by time-sampling into a single
  /// value using a specified function. The operation generates keys starting at the first
  /// key in the source series, using the specified `interval` and then obtains chunks based on 
  /// these keys in a fashion similar to the `Series.resample` function.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `interval` - The interval between the individual samples 
  ///  - `dir` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///  - `f` - A function that is called to aggregate each chunk into a single value.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  /// 
  /// [category:Lookup, resampling and scaling]
  let inline sampleTimeInto interval dir f (series:Series< ^K , ^V >) = 
    let add dt ts = (^K: (static member (+) : ^K * TimeSpan -> ^K) (dt, ts))
    Implementation.sampleTimeIntoInternal add None interval dir (fun _ -> f) series

  /// Performs sampling by time and aggregates chunks obtained by time-sampling into a single
  /// value using a specified function. The operation generates keys starting at the given
  /// `start` time, using the specified `interval` and then obtains chunks based on these
  /// keys in a fashion similar to the `Series.resample` function.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `start` - The initial time to be used for sampling
  ///  - `interval` - The interval between the individual samples 
  ///  - `dir` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///  - `f` - A function that is called to aggregate each chunk into a single value.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  /// 
  /// [category:Lookup, resampling and scaling]
  let inline sampleTimeAtInto start interval dir f (series:Series< ^K , ^V >) = 
    let add dt ts = (^K: (static member (+) : ^K * TimeSpan -> ^K) (dt, ts))
    Implementation.sampleTimeIntoInternal add (Some start) interval dir (fun _ -> f) series

  /// Performs sampling by time and returns chunks obtained by time-sampling as a nested  
  /// series. The operation generates keys starting at the first key in the source series,
  /// using the specified `interval` and then obtains chunks based on these
  /// keys in a fashion similar to the `Series.resample` function.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `interval` - The interval between the individual samples 
  ///  - `dir` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  /// 
  /// [category:Lookup, resampling and scaling]
  let inline sampleTime interval dir series = sampleTimeInto interval dir id series

  /// Performs sampling by time and returns chunks obtained by time-sampling as a nested  
  /// series. The operation generates keys starting at the given `start` time, using the 
  /// specified `interval` and then obtains chunks based on these
  /// keys in a fashion similar to the `Series.resample` function.
  ///
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `start` - The initial time to be used for sampling
  ///  - `interval` - The interval between the individual samples 
  ///  - `dir` - If this parameter is `Direction.Forward`, then each key is
  ///    used as the smallest key in a chunk; for `Direction.Backward`, the keys are
  ///    used as the greatest keys in a chunk.
  ///
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  /// 
  /// [category:Lookup, resampling and scaling]
  let inline sampleTimeAt start interval dir series = sampleTimeAtInto start interval dir id series

  /// Finds values at, or near, the specified times in a given series. The operation generates
  /// keys starting from the smallest key of the original series, using the specified `interval`
  /// and then finds values close to such keys using the specified `lookup` and `dir`.
  /// 
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `interval` - The interval between the individual samples 
  ///  - `dir` - Specifies how the keys should be generated. `Direction.Forward` means that the 
  ///    key is the smallest value of each chunk (and so first key of the series is returned and 
  ///    the last is not, unless it matches exactly _start + k*interval_); `Direction.Backward`
  ///    means that the first key is skipped and sample is generated at, or just before the end 
  ///    of interval and at the end of the series.
  ///  - `lookup` - Specifies how the lookup based on keys is performed. `Exact` means that the
  ///    values at exact keys will be returned; `NearestGreater` returns the nearest greater key value
  ///    (starting at the first key) and `NearestSmaller` returns the nearest smaller key value
  ///    (starting at most `interval` after the end of the series)
  /// 
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  /// 
  /// [category:Lookup, resampling and scaling]
  let inline lookupTime interval dir lookup (series:Series< ^K , ^V >) = 
    let add dt ts = (^K: (static member (+) : ^K * TimeSpan -> ^K) (dt, ts))
    Implementation.lookupTimeInternal add None interval dir lookup series

  /// Finds values at, or near, the specified times in a given series. The operation generates
  /// keys starting at the specified `start` time, using the specified `interval`
  /// and then finds values close to such keys using the specified `lookup` and `dir`.
  /// 
  /// ## Parameters
  ///  - `series` - An input series to be resampled
  ///  - `start` - The initial time to be used for sampling
  ///  - `interval` - The interval between the individual samples 
  ///  - `dir` - Specifies how the keys should be generated. `Direction.Forward` means that the 
  ///    key is the smallest value of each chunk (and so first key of the series is returned and 
  ///    the last is not, unless it matches exactly _start + k*interval_); `Direction.Backward`
  ///    means that the first key is skipped and sample is generated at, or just before the end 
  ///    of interval and at the end of the series.
  ///  - `lookup` - Specifies how the lookup based on keys is performed. `Exact` means that the
  ///    values at exact keys will be returned; `NearestGreater` returns the nearest greater key value
  ///    (starting at the first key) and `NearestSmaller` returns the nearest smaller key value
  ///    (starting at most `interval` after the end of the series)
  /// 
  /// ## Remarks
  /// This operation is only supported on ordered series. The method throws
  /// `InvalidOperationException` when the series is not ordered. 
  /// 
  /// [category:Lookup, resampling and scaling]
  let inline lookupTimeAt start interval dir lookup (series:Series< ^K , ^V >) = 
    let add dt ts = (^K: (static member (+) : ^K * TimeSpan -> ^K) (dt, ts))
    Implementation.lookupTimeInternal add (Some start) interval dir lookup series


  // ----------------------------------------------------------------------------------------------
  // Appending, joining and zipping
  // ----------------------------------------------------------------------------------------------

  /// [category:Appending, joining and zipping]
  let append (series1:Series<'K, 'V>) (series2:Series<'K, 'V>) =
   series1.Append(series2)

  /// [category:Appending, joining and zipping]
  let zipAlign kind lookup (series1:Series<'K, 'V1>) (series2:Series<'K, 'V2>) =
   series1.Zip(series2, kind, lookup)

  /// [category:Appending, joining and zipping]
  let zip (series1:Series<'K, 'V1>) (series2:Series<'K, 'V2>) =
   series1.Zip(series2)

  /// [category:Appending, joining and zipping]
  let zipInner (series1:Series<'K, 'V1>) (series2:Series<'K, 'V2>) =
   series1.ZipInner(series2)
    
  /// [category:Joining, zipping and appending]
  let inline zipAlignInto kind lookup (op:'V1->'V2->'R) (series1:Series<'K, 'V1>) (series2:Series<'K, 'V2>) : Series<'K, 'R> =
    let joined = series1.Zip(series2, kind, lookup)
    joined.SelectOptional(fun (KeyValue(_, v)) -> 
      match v with
      | OptionalValue.Present(OptionalValue.Present a, OptionalValue.Present b) -> 
          OptionalValue(op a b)
      | _ -> OptionalValue.Missing )

  /// [category:Joining, zipping and appending]
  let inline zipInto (op:'V1->'V2->'R) (series1:Series<'K, 'V1>) (series2:Series<'K, 'V2>) : Series<'K, 'R> =
    zipAlignInto JoinKind.Inner Lookup.Exact op (series1:Series<'K, 'V1>) (series2:Series<'K, 'V2>)

  /// [category:Joining, zipping and appending]
  let union (series1:Series<'K, 'V>) (series2:Series<'K, 'V>) = 
    series1.Union(series2)

  /// [category:Joining, zipping and appending]
  let unionUsing behavior (series1:Series<'K, 'V>) (series2:Series<'K, 'V>) = 
    series1.Union(series2, behavior)
