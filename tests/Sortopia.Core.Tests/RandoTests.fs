namespace Sortopia.Core.Tests

open System
open FSharp.UMX
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Core.RandomSeed

type RandoTests() =

    // Helper function for chi-squared test for uniformity
    let chiSquaredTest (values: int[]) buckets =
        let n = values.Length
        let expected = float n / float buckets
        let counts = Array.zeroCreate buckets
        for v in values do
            counts.[v] <- counts.[v] + 1
        let chi2 =
            counts
            |> Array.map (fun count -> (float count - expected) ** 2.0 / expected)
            |> Array.sum
        // Critical value for 10% significance level, degrees of freedom = buckets - 1
        let criticalValues = Map [
            9, 18.307 // 10 buckets - 1, 10% significance
            99, 123.225 // 100 buckets - 1
        ]
        chi2, Map.tryFind (buckets - 1) criticalValues

    // Test randomLcg with explicit UMX tagging
    [<Theory>]
    [<InlineData(42UL)>]
    [<InlineData(123456789UL)>]
    let ``randomLcg produces values in expected ranges`` (seed: uint64) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng = randomLcg taggedSeed :> IRando
        let uintVal = rng.NextUInt()
        let intVal = rng.NextPositiveInt()
        let floatVal = rng.NextFloat()
        let indexVal = rng.NextIndex 10
        let ulongVal = rng.NextULong()
        let guidVal = rng.NextGuid()
    
        Assert.InRange(uintVal, 0u, UInt32.MaxValue)
        Assert.InRange(intVal, 0, Int32.MaxValue)
        Assert.InRange(floatVal, 0.0, 1.0 - Double.Epsilon)
        Assert.InRange(indexVal, 0, 9)
        Assert.InRange(ulongVal, 0UL, UInt64.MaxValue)
        Assert.NotEqual(Guid.Empty, guidVal)
        Assert.Equal(40, rng.ByteCount)

    [<Theory>]
    [<InlineData(42)>]
    [<InlineData(123456789)>]
    let ``randomNet produces values in expected ranges`` (seed: int32) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng = randomNet taggedSeed :> IRando
        let uintVal = rng.NextUInt()
        let intVal = rng.NextPositiveInt()
        let floatVal = rng.NextFloat()
        let indexVal = rng.NextIndex 10
        let ulongVal = rng.NextULong()
        let guidVal = rng.NextGuid()
    
        Assert.InRange(uintVal, 0u, UInt32.MaxValue)
        Assert.InRange(intVal, 0, Int32.MaxValue)
        Assert.InRange(floatVal, 0.0, 1.0 - Double.Epsilon)
        Assert.InRange(indexVal, 0, 9)
        Assert.InRange(ulongVal, 0UL, UInt64.MaxValue)
        Assert.NotEqual(Guid.Empty, guidVal)
        Assert.Equal(44, rng.ByteCount)

    [<Theory>]
    [<InlineData(42UL)>]
    [<InlineData(123456789UL)>]
    let ``randomLcg is deterministic with same seed`` (seed: uint64) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng1 = randomLcg taggedSeed :> IRando
        let rng2 = randomLcg taggedSeed :> IRando
        let values1: list<uint32> = [ for _ in 1..100 -> rng1.NextUInt() ]
        let values2: list<uint32> = [ for _ in 1..100 -> rng2.NextUInt() ]
        Assert.Equal<list<uint32>>(values1, values2)
        Assert.Equal(400, rng1.ByteCount)
        Assert.Equal(400, rng2.ByteCount)

    [<Theory>]
    [<InlineData(42)>]
    [<InlineData(123456789)>]
    let ``randomNet is deterministic with same seed`` (seed: int32) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng1 = randomNet taggedSeed :> IRando
        let rng2 = randomNet taggedSeed :> IRando
        let values1: list<uint32> = [ for _ in 1..100 -> rng1.NextUInt() ]
        let values2: list<uint32> = [ for _ in 1..100 -> rng2.NextUInt() ]
        Assert.Equal<list<uint32>>(values1, values2)
        Assert.Equal(400, rng1.ByteCount)
        Assert.Equal(400, rng2.ByteCount)

    [<Theory>]
    [<InlineData(42UL)>]
    [<InlineData(123456789UL)>]
    let ``randomLcg produces uniform distribution for NextIndex`` (seed: uint64) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng = randomLcg taggedSeed :> IRando
        let n = 100000
        let modulus = 10
        let values = [| for _ in 1..n -> rng.NextIndex modulus |]
        let chi2, criticalValue = chiSquaredTest values modulus
        match criticalValue with
        | Some cv -> Assert.True(chi2 < cv, $"Chi-squared value {chi2} exceeds critical value {cv} for uniform distribution")
        | None -> Assert.Fail("No critical value defined for chi-squared test")
        Assert.Equal(n * 4, rng.ByteCount)

    [<Theory>]
    [<InlineData(42)>]
    [<InlineData(123456789)>]
    let ``randomNet produces uniform distribution for NextIndex`` (seed: int32) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng = randomNet taggedSeed :> IRando
        let n = 100000
        let modulus = 10
        let values = [| for _ in 1..n -> rng.NextIndex modulus |]
        let chi2, criticalValue = chiSquaredTest values modulus
        match criticalValue with
        | Some cv -> Assert.True(chi2 < cv, $"Chi-squared value {chi2} exceeds critical value {cv} for uniform distribution")
        | None -> Assert.Fail("No critical value defined for chi-squared test")
        Assert.Equal(n * 4, rng.ByteCount)

    [<Theory>]
    [<InlineData(42UL)>]
    [<InlineData(123456789UL)>]
    let ``randomLcg generates unique GUIDs`` (seed: uint64) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng = randomLcg taggedSeed :> IRando
        let guids = [ for _ in 1..1000 -> rng.NextGuid() ]
        let distinctGuids = guids |> List.distinct
        Assert.Equal(guids.Length, distinctGuids.Length)
        Assert.Equal(1000 * 16, rng.ByteCount)

    [<Theory>]
    [<InlineData(42)>]
    [<InlineData(123456789)>]
    let ``randomNet generates unique GUIDs`` (seed: int32) =
        let taggedSeed = UMX.tag<randomSeed> seed
        let rng = randomNet taggedSeed :> IRando
        let guids = [ for _ in 1..1000 -> rng.NextGuid() ]
        let distinctGuids = guids |> List.distinct
        Assert.Equal(guids.Length, distinctGuids.Length)
        Assert.Equal(1000 * 16, rng.ByteCount)

    [<Fact>]
    let ``randomLcg and randomNet produce different sequences`` () =
        let seed = 42UL
        let lcg = randomLcg (UMX.tag<randomSeed> seed) :> IRando
        let net = randomNet (UMX.tag<randomSeed> 42) :> IRando
        let lcgValues: list<uint32> = [ for _ in 1..100 -> lcg.NextUInt() ]
        let netValues: list<uint32> = [ for _ in 1..100 -> net.NextUInt() ]
        Assert.NotEqual<list<uint32>>(lcgValues, netValues)

    // Test cases
    [<Fact>]
    let ``fromGuid produces different outputs for different Guids`` () =
        let guid1 = Guid("123e4567-e89b-12d3-a456-426614174000")
        let guid2 = Guid("987fcdeb-0123-45ab-cdef-0123456789ab")
        let seed1 = fromGuid guid1
        let seed2 = fromGuid guid2
        seed1 |> should not' (equal seed2)

    [<Fact>]
    let ``fromGuid with alternating byte array produces zero seed`` () =
        let bytes = [| 0xAAuy; 0x55uy; 0xAAuy; 0x55uy; 0xAAuy; 0x55uy; 0xAAuy; 0x55uy;
                       0xAAuy; 0x55uy; 0xAAuy; 0x55uy; 0xAAuy; 0x55uy; 0xAAuy; 0x55uy |]
        let guid = Guid(bytes)
        let seed1 = fromGuid guid
        let seed2 = fromGuid guid
        seed1 |> should equal seed2
        UMX.untag<randomSeed> seed1 |> should equal 0UL

    [<Fact>]
    let ``fromGuid with incremental byte array uses all bytes`` () =
        let bytes = [| 0x01uy; 0x02uy; 0x03uy; 0x04uy; 0x05uy; 0x06uy; 0x07uy; 0x08uy;
                       0x09uy; 0x0Auy; 0x0Buy; 0x0Cuy; 0x0Duy; 0x0Euy; 0x0Fuy; 0x10uy |]
        let guid = Guid(bytes)
        let seed = fromGuid guid
        let expected = (uint64 0x01 <<< 0) ^^^ (uint64 0x02 <<< 8) ^^^ (uint64 0x03 <<< 16) ^^^ 
                       (uint64 0x04 <<< 24) ^^^ (uint64 0x05 <<< 32) ^^^ (uint64 0x06 <<< 40) ^^^ 
                       (uint64 0x07 <<< 48) ^^^ (uint64 0x08 <<< 56) ^^^ 
                       (uint64 0x09 <<< 0) ^^^ (uint64 0x0A <<< 8) ^^^ (uint64 0x0B <<< 16) ^^^ 
                       (uint64 0x0C <<< 24) ^^^ (uint64 0x0D <<< 32) ^^^ (uint64 0x0E <<< 40) ^^^ 
                       (uint64 0x0F <<< 48) ^^^ (uint64 0x10 <<< 56)
        UMX.untag<randomSeed> seed |> should equal expected

    [<Fact>]
    let ``fromGuid with single byte change produces different seed`` () =
        let bytes1 = [| 0xFFuy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy;
                        0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy |]
        let bytes2 = [| 0xFFuy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy;
                        0x01uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0x00uy |]
        let guid1 = Guid(bytes1)
        let guid2 = Guid(bytes2)
        let seed1 = fromGuid guid1
        let seed2 = fromGuid guid2
        seed1 |> should not' (equal seed2)
