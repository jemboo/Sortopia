namespace Sortopia.Core

open System.Threading


[<AutoOpen>]
module ComputationBuilders =


    /// Helper to bridge CancellationTokens into the AsyncResult flow
    let checkCancellation (token: CancellationToken) : Async<Result<unit, string>> =
        async {
            if token.IsCancellationRequested then 
                return Error "Operation cancelled"
            else 
                return Ok ()
        }

    type MaybeBuilder() =
        member _.Bind(x, f) = Option.bind f x
        member _.Return(x) = Some x
        member _.ReturnFrom(x) = x
        member _.Zero() = Some ()
    
        // The "unit -> 'a" function is the delay the compiler creates
        member _.Delay(f) = f() 
    
        // Now Combine takes the result of Delay (the value) directly
        member _.Combine(step1, step2) = 
            match step1 with
            | None -> None
            | Some () -> step2

    let maybe = MaybeBuilder()


    type AsyncResultBuilder() =

        member this.Zero() = async { return Ok () }

        member this.Return(v) = async { return Ok v }
    
        member this.ReturnFrom(x: Async<Result<'T, 'E>>) = x
    
        member this.ReturnFrom(x: Result<'T, 'E>) = async { return x }

        member this.Bind(x: Async<Result<'T, 'E>>, f: 'T -> Async<Result<'U, 'E>>) =
            async {
                let! res = x
                match res with
                | Error e -> return Error e
                | Ok v -> return! f v
            }

        member this.Bind(x: Result<'T, 'E>, f: 'T -> Async<Result<'U, 'E>>) =
            match x with
            | Error e -> async { return Error e }
            | Ok v -> f v

        // Allows: let! x = standardAsync (returns Ok x)
        member this.Bind(x: Async<'T>, f: 'T -> Async<Result<'U, 'E>>) =
            async {
                let! res = x
                return! f res
            }

        member this.Delay(f: unit -> 'T) = f

        member this.Run(f: unit -> Async<Result<'T, 'E>>) = f()

        member this.Combine(step1: Async<Result<unit, 'E>>, step2: unit -> Async<Result<'T, 'E>>) : Async<Result<'T, 'E>> =
            async {
                let! res1 = step1
                match res1 with
                | Ok () -> return! step2()
                | Error e -> return Error e
            }

        member this.While(guard: unit -> bool, body: unit -> Async<Result<unit, 'E>>) : Async<Result<unit, 'E>> =
            if not (guard()) then 
                this.Zero()
            else 
                this.Combine(body(), fun () -> this.While(guard, body))


    // The # symbol means "This type or any subtype" (Flexible Type)
        member this.Using(resource: #System.IDisposable, binder: #System.IDisposable -> Async<Result<'V, 'E>>) =
            async {
                use _d = resource
                return! binder resource
            }

        member this.For(sequence: seq<'T>, body: 'T -> Async<Result<unit, 'E>>) : Async<Result<unit, 'E>> =
            // Use the 'use' keyword directly or ensure Using doesn't over-constrain the enum
            this.Using(sequence.GetEnumerator(), fun enum ->
                this.While((fun () -> enum.MoveNext()), (fun () -> body enum.Current)))


        member this.TryWith(computation: Async<Result<'T, 'E>>, handler: exn -> Async<Result<'T, 'E>>) =
            async.TryWith(computation, handler)

        member this.TryFinally(computation: Async<Result<'T, 'E>>, compensation: unit -> unit) =
            async.TryFinally(computation, compensation)


        /// Handles 'try ... with' for delayed computations
        member this.TryWith(computation: unit -> Async<Result<'T, 'E>>, handler: exn -> Async<Result<'T, 'E>>) : Async<Result<'T, 'E>> =
            async {
                try 
                    return! computation()
                with e -> 
                    return! handler e
            }

        /// Handles 'try ... finally' for delayed computations
        member this.TryFinally(computation: unit -> Async<Result<'T, 'E>>, compensation: unit -> unit) : Async<Result<'T, 'E>> =
            async {
                try 
                    return! computation()
                finally 
                    compensation()
            }


    let asyncResult = AsyncResultBuilder()


[<RequireQualifiedAccess>]
module Async =
    /// Transforms the result of an async computation
    let map (f: 'T -> 'U) (operation: Async<'T>) : Async<'U> =
        async {
            let! result = operation
            return f result
        }


[<RequireQualifiedAccess>]
module AsyncResult =
    
    let bind (f: 'T -> Async<Result<'U, 'E>>) (x: Async<Result<'T, 'E>>) : Async<Result<'U, 'E>> =
        async {
            let! res = x
            match res with
            | Error e -> return Error e
            | Ok v -> return! f v
        }


    let map (f: 'T -> 'U) (x: Async<Result<'T, 'E>>) : Async<Result<'U, 'E>> =
        async {
            let! res = x
            return Result.map f res
        }

    let mapError (f: 'E -> 'F) (x: Async<Result<'T, 'E>>) : Async<Result<'T, 'F>> =
        async {
            let! res = x
            return Result.mapError f res
        }


module Result =
    let ofOption errorMsg = function
        | Some x -> Ok x
        | None   -> Error errorMsg
