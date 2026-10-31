namespace Sortopia.Permutations.Tests

open System.Collections.Generic
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Permutations.Orbit

// Custom equality comparer for Orbit to use in HashSet
type OrbitEqualityComparer() =
    interface IEqualityComparer<orbit> with
        member _.Equals(x: orbit, y: orbit) = x.Equals(y)
        member _.GetHashCode(orbit: orbit) = orbit.GetHashCode()

type OrbitTests() =

    [<Fact>]
    let ``Create valid orbit with unique non-negative indices`` () =
        let indices = [2; 0; 1]
        let orbit = orbit.create indices
        orbit.Indices |> should equal [0; 1; 2] // Normalized with smallest index first

    [<Fact>]
    let ``Create empty orbit`` () =
        let orbit = orbit.create []
        orbit.Indices.Length |> should equal 0

    [<Fact>]
    let ``Fail to create orbit with negative indices`` () =
        let indices = [1; -2; 3]
        (fun () -> orbit.create indices |> ignore) |> should throw typeof<System.Exception>

    [<Fact>]
    let ``Fail to create orbit with duplicate indices`` () =
        let indices = [1; 2; 1]
        (fun () -> orbit.create indices |> ignore) |> should throw typeof<System.Exception>

    [<Fact>]
    let ``Orbits with same normalized indices are equal`` () =
        let orbit1 = orbit.create [2; 0; 1]
        let orbit2 = orbit.create [0; 1; 2]
        orbit1.Equals(orbit2) |> should be True
        orbit1.GetHashCode() |> should equal (orbit2.GetHashCode())

    [<Fact>]
    let ``Orbits with different indices are not equal`` () =
        let orbit1 = orbit.create [0; 1]
        let orbit2 = orbit.create [0; 2]
        orbit1.Equals(orbit2) |> should be False

    [<Fact>]
    let ``Get reflection of orbit`` () =
        let orbit = orbit.create [0; 1; 2]
        let reflected = getReflection 3 orbit
        reflected.Indices |> should equal [0; 2; 1] // Reflection about (3-1)/2 = 1

    [<Fact>]
    let ``Get reflection of orbit with order 4`` () =
        let orbit = orbit.create [0; 2]
        let reflected = getReflection 4 orbit
        reflected.Indices |> should equal [1; 3] // Reflection: 0 -> 3, 2 -> 1


    [<Fact>]
    let ``Orbits in HashSet collection handle equality correctly`` () =
        let orbit1 = orbit.create [2; 5; 0]
        let orbit2 = orbit.create [5; 0; 2]
        let orbit3 = orbit.create [1; 3; 4]
        let orbitSet = HashSet<orbit>(OrbitEqualityComparer())
        orbitSet.Add orbit1 |> should be True
        orbitSet.Add orbit2 |> should be False // orbit2 is equal to orbit1, so not added
        orbitSet.Add orbit3 |> should be True  // orbit3 is distinct, so added
        orbitSet.Count |> should equal 2
        orbitSet.Contains orbit1 |> should be True
        orbitSet.Contains orbit2 |> should be True
        orbitSet.Contains orbit3 |> should be True

    [<Fact>]
    let ``GetHashCode is consistent across equal orbits`` () =
        let orbit1 = orbit.create [2; 5; 0]
        let orbit2 = orbit.create [5; 0; 2]
        let orbit3 = orbit.create [0; 2; 5]
        orbit1.GetHashCode() |> should equal (orbit2.GetHashCode())
        orbit1.GetHashCode() |> should equal (orbit3.GetHashCode())