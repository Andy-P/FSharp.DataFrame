﻿module Main
#if INTERACTIVE
#I "..\\bin"
#load "DataFrame.fsx"
#endif

open System
open System.Linq.Expressions
open FSharp.DataFrame.Tests
open FSharp.DataFrame

// ------------------------------------------------------------------------------------------------
// Simple "test runner" for running tests in your IDE with just Run 
// (useful for performance analysis in VS)
// ------------------------------------------------------------------------------------------------

// Set console color temporarilly
let colored color = 
  let prev = Console.ForegroundColor
  Console.ForegroundColor <- color
  { new IDisposable with
      member x.Dispose() = Console.ForegroundColor <- prev }

// Measure how long does a test take
let timed f = 
  let sw = System.Diagnostics.Stopwatch.StartNew()
  let res = f()
  printfn "%dms" sw.ElapsedMilliseconds
  res

// Measure how long does a test take
let timedFormat f fmt arg = 
  let sw = System.Diagnostics.Stopwatch.StartNew()
  let res = f()
  printfn fmt sw.ElapsedMilliseconds arg
  res

// Find all referenced tests
let testAll () =
  let asm = System.Reflection.Assembly.Load("FSharp.DataFrame.Tests")
  let tests =
    [ for typ in asm.GetTypes() do
        for mi in typ.GetMethods() do
          if mi.GetCustomAttributes(typeof<NUnit.Framework.TestAttribute>, false).Length <> 0 then
            yield typ.Name, mi ]
    |> Seq.groupBy fst

  // Run all tests and measure time
  for unit, tests in tests do
    ( use _n = colored ConsoleColor.White
      printfn "\nTESTING: %s" unit )
    for _, test in tests do 
      let call = Expression.Lambda<Action>(Expression.Call(null, test)).Compile()
      try 
        use _n = colored ConsoleColor.Green
        timedFormat call.Invoke " - [%dms] %s" test.Name
      with e -> 
        ( use _n = colored ConsoleColor.Red
          printfn " - %s (FAILED)" test.Name )
        ( use _n = colored ConsoleColor.Gray
          printfn "%A\n" e )

// ------------------------------------------------------------------------------------------------
// Run all tests, or run a single test
// ------------------------------------------------------------------------------------------------

let testOne() =
  for i in 1 .. 6 do
    timed(fun () -> 
      CSharp.Tests.DynamicFrameTests.CanAddSeriesDynamically()
      CSharp.Tests.DynamicFrameTests.CanGetSeriesDynamically()
    )

//do testAll()
do testOne()

