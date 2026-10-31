namespace Sortopia.Core
open System
open FSharp.UMX


[<Measure>] type randomSeed
[<Measure>] type rngFactoryId


module RandomSeed =
    let fromGuid(guid: Guid) : uint64<randomSeed> =
            let bytes = guid.ToByteArray()
        
            // Directly pull the lower and upper 64-bit halves of the GUID payload
            let lowerHalf = BitConverter.ToUInt64(bytes, 0)
            let upperHalf = BitConverter.ToUInt64(bytes, 8)
        
            // XOR compress them together instantly via CPU registers
            let finalSeed = lowerHalf ^^^ upperHalf
        
            UMX.tag<randomSeed> finalSeed


    let toString(value: uint64<randomSeed>) : string =
         UmxExt.uint64OptionToString (Some value)

    let fromString(value: string) : uint64<randomSeed> =
        match UInt64.TryParse(value) with
        | true, parsedValue -> UMX.tag<randomSeed> parsedValue
        | false, _ -> failwithf "Invalid randomSeed string: %s" value


type rngType =
    | Lcg
    | Net
    | Smx 


module RngType =
    let toString = function
        | Lcg -> "LCG"
        | Net -> "Net"
        | Smx -> "SplitMix64"

    let fromString = function
        | "LCG" -> Lcg
        | "Net" -> Net
        | "SplitMix64" -> Smx
        | s -> failwithf "Unknown rngType: %s" s


type IRando =
    abstract member NextIndex: int -> int
    abstract member NextUInt: unit -> uint32
    abstract member NextPositiveInt: unit -> int32
    abstract member NextULong: unit -> uint64
    abstract member NextFloat: unit -> float
    abstract member NextGuid: unit -> Guid
    abstract member ByteCount: int
    abstract member rngType: rngType


// ──────────────────────────────────────────────
// SplitMix64 Implementation (Recommended)
// ──────────────────────────────────────────────
type randomSplitMix64(seed: uint64<randomSeed>) =
    let mutable _state = seed |> UMX.untag |> uint64
    let mutable _byteCount = 0

    let nextULong() =
        _state <- _state + 0x9E3779B97F4A7C15UL
        let mutable z = _state
        z <- (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
        z <- (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
        z ^^^ (z >>> 31)

    member this.Seed = seed
    member this.ByteCount = _byteCount

    member this.NextUInt =
        _byteCount <- _byteCount + 4
        uint32 (nextULong() >>> 32)

    member this.NextULong =
        _byteCount <- _byteCount + 8
        nextULong()

    member this.NextFloat =
        // Full 53-bit precision
        let mantissa = (this.NextULong >>> 11) ||| 0x3FF0000000000000UL
        (float mantissa) - 1.0

    member this.AsNextUInt() = fun () -> this.NextUInt

    interface IRando with
        member this.NextUInt () = this.NextUInt
        member this.NextPositiveInt () =
            int (this.NextUInt % uint32 Int32.MaxValue)
        member this.NextIndex (modulus: int) =
            if modulus <= 0 then
                int this.NextUInt
            else
                let threshold = UInt32.MaxValue - (UInt32.MaxValue % uint32 modulus)
                let mutable result = None
                while result.IsNone do
                    let r = this.NextUInt
                    if r < threshold then
                        result <- Some (int (r % uint32 modulus))
                result.Value
        member this.NextULong () = this.NextULong
        member this.NextGuid () =
            let b1 = this.NextUInt
            let b2 = this.NextUInt
            let b3 = this.NextUInt
            let b4 = this.NextUInt
            Guid(int b1, int16 b2, int16 (b2 >>> 16), [|
                byte b3; byte (b3 >>> 8); byte (b3 >>> 16); byte (b3 >>> 24);
                byte b4; byte (b4 >>> 8); byte (b4 >>> 16); byte (b4 >>> 24)
            |])
        member this.NextFloat () = this.NextFloat
        member this.ByteCount = this.ByteCount
        member this.rngType = Smx


// ──────────────────────────────────────────────
// Linear Congruential Generator (LCG) 
// ──────────────────────────────────────────────
/// <summary>
/// A Linear Congruential Generator (LCG) for random number generation.
/// Uses parameters a = 6364136223846793005, c = 1442695040888963407, m = 2^64.
/// </summary>
type randomLcg(seed: uint64<randomSeed>) =
    let _a = 6364136223846793005UL
    let _c = 1442695040888963407UL
    let mutable _last = (_a * (uint64 (seed |> UMX.untag)) + _c)
    let mutable _byteCount = 0
    
    /// <summary>
    /// Gets the initial seed.
    /// </summary>
    member this.Seed = seed
    
    /// <summary>
    /// Gets the number of random bytes generated.
    /// </summary>
    member this.ByteCount = _byteCount
    
    /// <summary>
    /// Generates a random uint32 using the upper 32 bits of the LCG state.
    /// </summary>
    member this.NextUInt =
        _byteCount <- _byteCount + 4
        _last <- (_a * _last + _c)
        uint32 (_last >>> 32)
    
    /// <summary>
    /// Generates a random uint64 by combining two uint32 values.
    /// </summary>
    member this.NextULong =
        let high = uint64 this.NextUInt <<< 32
        let low = uint64 this.NextUInt
        high ||| low
    
    /// <summary>
    /// Generates a random float in [0, 1).
    /// </summary>
    member this.NextFloat =
        (float this.NextUInt) / 4294967295.0
    
    /// <summary>
    /// Returns a function that generates random uint32 values.
    /// </summary>
    member this.AsNextUInt() = fun () -> this.NextUInt
    
    interface IRando with
        member this.NextUInt () = this.NextUInt
        member this.NextPositiveInt () =
            int (this.NextUInt % uint32 Int32.MaxValue)
        member this.NextIndex (modulus: int) =
            if modulus <= 0 then
                int this.NextUInt
            else
                let threshold = UInt32.MaxValue - (UInt32.MaxValue % uint32 modulus)
                let mutable result = None
                while result.IsNone do
                    let r = this.NextUInt
                    if r < threshold then
                        result <- Some (int (r % uint32 modulus))
                result.Value
        member this.NextULong () = this.NextULong
        member this.NextGuid () =
            let b1 = this.NextUInt
            let b2 = this.NextUInt
            let b3 = this.NextUInt
            let b4 = this.NextUInt
            Guid(int b1, int16 b2, int16 (b2 >>> 16), [|
                byte b3; byte (b3 >>> 8); byte (b3 >>> 16); byte (b3 >>> 24);
                byte b4; byte (b4 >>> 8); byte (b4 >>> 16); byte (b4 >>> 24)
            |])
        member this.NextFloat () = this.NextFloat
        member this.ByteCount = this.ByteCount
        member this.rngType = Lcg


// ──────────────────────────────────────────────
// .NET Random Generator (LCG) 
// ──────────────────────────────────────────────
/// <summary>
/// A random number generator using System.Random.
/// </summary>
type randomNet(seed: int32<randomSeed>) =
    let _random = Random(int (UMX.untag seed))
    let mutable _byteCount = 0
    
    /// <summary>
    /// Gets the initial seed.
    /// </summary>
    member this.Seed = seed
    
    /// <summary>
    /// Gets the number of random bytes generated.
    /// </summary>
    member this.ByteCount = _byteCount
    
    /// <summary>
    /// Generates a random uint32 using System.Random.
    /// </summary>
    member this.NextUInt =
        _byteCount <- _byteCount + 4
        let bytes = Array.zeroCreate<byte> 4
        _random.NextBytes(bytes)
        BitConverter.ToUInt32(bytes, 0)
    
    /// <summary>
    /// Generates a random uint64 using System.Random.
    /// </summary>
    member this.NextULong =
        _byteCount <- _byteCount + 8
        let bytes = Array.zeroCreate<byte> 8
        _random.NextBytes(bytes)
        BitConverter.ToUInt64(bytes, 0)
    
    /// <summary>
    /// Generates a random float in [0, 1) using System.Random.
    /// </summary>
    member this.NextFloat =
        _byteCount <- _byteCount + 8
        let bytes = Array.zeroCreate<byte> 8
        _random.NextBytes(bytes)
        let value = BitConverter.ToDouble(bytes, 0)
        // Scale to [0, 1)
        value / Double.MaxValue
        // Alternatively, could use _random.NextDouble() directly, but using NextBytes for consistency
        // _random.NextDouble()
    
    /// <summary>
    /// Returns a function that generates random uint32 values.
    /// </summary>
    member this.AsNextUInt() = fun () -> this.NextUInt
    
    interface IRando with
        member this.NextUInt () = this.NextUInt
        member this.NextPositiveInt () =
            _byteCount <- _byteCount + 4
            let bytes = Array.zeroCreate<byte> 4
            _random.NextBytes(bytes)
            int (BitConverter.ToUInt32(bytes, 0) % uint32 Int32.MaxValue)
        member this.NextIndex (modulus: int) =
            _byteCount <- _byteCount + 4
            let bytes = Array.zeroCreate<byte> 4
            _random.NextBytes(bytes)
            bytes.[3] <- (bytes.[3] >>> 1)
            let value = BitConverter.ToUInt32(bytes, 0)
            if modulus <= 0 then
                int value
            else
                int value % modulus
        member this.NextULong () = this.NextULong
        member this.NextGuid () =
            _byteCount <- _byteCount + 16
            let bytes = Array.zeroCreate<byte> 16
            _random.NextBytes(bytes)
            Guid(bytes)
        member this.NextFloat () = this.NextFloat
        member this.ByteCount = this.ByteCount
        member this.rngType = Net


module Rando =


    let create (rngType: rngType) (guid: Guid) : IRando =
        let seed = RandomSeed.fromGuid guid
        match rngType with
        | Lcg -> randomLcg(seed) :> IRando
        | Net -> randomNet(UMX.tag<randomSeed> (int32 seed)) :> IRando
        | Smx -> randomSplitMix64(seed) :> IRando


    type MockRando(nextFloatValues: float list, nextIndexValues: int list) =
        let mutable floats = nextFloatValues
        let mutable indices = nextIndexValues
        interface IRando with
            member this.rngType: rngType = 
                raise (System.NotImplementedException())
            member _.NextFloat() =
                match floats with
                | x :: xs ->
                    floats <- xs
                    x
                | _ -> failwith "No more float values available"
            member _.NextIndex(max) =
                match indices with
                | x :: xs when x < max ->
                    indices <- xs
                    x
                | _ -> failwith "No more index values available"
            member _.NextUInt() = failwith "Not implemented"
            member _.NextPositiveInt() = failwith "Not implemented"
            member _.NextULong() = failwith "Not implemented"
            member _.NextGuid() = failwith "Not implemented"
            member _.ByteCount = 0


// ──────────────────────────────────────────────
// RngFactory
// ──────────────────────────────────────────────
[<CustomEquality; CustomComparison>]
type rngFactory =
    private
        { id: Guid<rngFactoryId>
          rngType: rngType
          create: Guid -> IRando }
    with
    member this.Id with get() = this.id
    member this.RngType with get() = this.rngType
    member this.Create(seed: Guid) : IRando = this.create seed

    override this.ToString() =
        sprintf "rngFactory(Id: %A, Type: %A)" this.id this.rngType

    override this.Equals(obj) =
        match obj with
        | :? rngFactory as other -> this.id = other.id && this.rngType = other.rngType
        | _ -> false

    override this.GetHashCode() = hash (this.id, this.rngType)

    interface System.IComparable with
        member this.CompareTo(obj) =
            match obj with
            | :? rngFactory as other -> compare this.id other.id
            | _ -> invalidArg "obj" "Cannot compare values of different types"

    // Satisfy the stable identity contract
    interface IStableSerializable with
        member this.WriteStableBytes writer =
            // 1. Write the factory Guid components
            let rawGuid = UMX.untag this.id
            writer.Write(rawGuid.ToByteArray())
            // 2. Write the structural name of the Rng type case (e.g. "Lcg", "Net", "Smx")
            writer.Write(sprintf "%A" this.rngType)


    static member private createFactory (id: Guid<rngFactoryId>) (rngType: rngType) : rngFactory =
        { id = id
          rngType = rngType
          create = fun guid -> Rando.create rngType guid }

    static member private LcgFactoryId = Guid.Parse("00000000-0000-0000-0000-000000000001") |> UMX.tag<rngFactoryId>
    static member private NetFactoryId = Guid.Parse("00000000-0000-0000-0000-000000000002") |> UMX.tag<rngFactoryId>
    static member private SmxFactoryId = Guid.Parse("00000000-0000-0000-0000-000000000003") |> UMX.tag<rngFactoryId>

    static member LcgFactory : rngFactory = rngFactory.createFactory rngFactory.LcgFactoryId Lcg
    static member NetFactory : rngFactory = rngFactory.createFactory rngFactory.NetFactoryId Net
    static member SmxFactory : rngFactory = rngFactory.createFactory rngFactory.SmxFactoryId Smx

    static member getFactory (rngType: rngType) : rngFactory =
        match rngType with
        | Lcg -> rngFactory.LcgFactory
        | Net -> rngFactory.NetFactory
        | Smx -> rngFactory.SmxFactory

module RngFactory =
    let create (rngType: rngType) : rngFactory =
        rngFactory.getFactory rngType
