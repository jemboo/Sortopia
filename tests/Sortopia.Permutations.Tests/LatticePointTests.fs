namespace Sortopia.Permutations.Tests
open Xunit
open Sortopia.Core
open Sortopia.Permutations

type LatticePointTests() =

    // Utility to create points more concisely
    let lp xs = latticePoint.create(xs)

    [<Fact>]
    let ``latticePoint equality works correctly`` () =
        let p1 = latticePoint.create [|1; 2; 3|]
        let p2 = latticePoint.create [|1; 2; 3|]
        let p3 = latticePoint.create [|1; 2; 4|]
        
        Assert.Equal(p1, p2)
        Assert.NotEqual(p1, p3)
    
    [<Fact>]
    let ``latticePoint hash code is consistent`` () =
        let p1 = latticePoint.create [|1; 2; 3|]
        let p2 = latticePoint.create [|1; 2; 3|]
        
        Assert.Equal(p1.GetHashCode(), p2.GetHashCode())
    
    [<Fact>]
    let ``dictionary order: basic comparison`` () =
        let p1 = latticePoint.create [|1; 2; 3|]
        let p2 = latticePoint.create [|1; 3; 1|]
        
        let result = (p1 :> System.IComparable).CompareTo(p2)
        
        Assert.True(result < 0, "p1 should be less than p2")
    
    [<Fact>]
    let ``dictionary order: first coordinate determines order`` () =
        let p1 = latticePoint.create [|1; 9; 9|]
        let p2 = latticePoint.create [|2; 0; 0|]
        
        let result = (p1 :> System.IComparable).CompareTo(p2)
        
        Assert.True(result < 0, "p1 should be less than p2")
    
    [<Fact>]
    let ``dictionary order: equal points return zero`` () =
        let p1 = latticePoint.create [|1; 2; 3|]
        let p2 = latticePoint.create [|1; 2; 3|]
        
        let result = (p1 :> System.IComparable).CompareTo(p2)
        
        Assert.Equal(0, result)
    
    [<Fact>]
    let ``dictionary order: shorter array with same prefix is less`` () =
        let p1 = latticePoint.create [|1; 2|]
        let p2 = latticePoint.create [|1; 2; 3|]
        
        let result = (p1 :> System.IComparable).CompareTo(p2)
        
        Assert.True(result < 0, "shorter array should be less than longer")
   
    
    [<Fact>]
    let ``Array.sort produces correct dictionary order`` () =
        let points = [|
            latticePoint.create [|3; 1; 4|]
            latticePoint.create [|1; 2; 3|]
            latticePoint.create [|1; 2; 1|]
            latticePoint.create [|2; 1; 1|]
        |]
        
        let sorted = Array.sort points
        
        Assert.Equal(latticePoint.create [|1; 2; 1|], sorted.[0])
        Assert.Equal(latticePoint.create [|1; 2; 3|], sorted.[1])
        Assert.Equal(latticePoint.create [|2; 1; 1|], sorted.[2])
        Assert.Equal(latticePoint.create [|3; 1; 4|], sorted.[3])
    
    [<Fact>]
    let ``List.sort works with latticePoint`` () =
        let points = [
            latticePoint.create [|3; 1|]
            latticePoint.create [|1; 5|]
            latticePoint.create [|1; 3|]
            latticePoint.create [|2; 1|]
        ]
        
        let sorted = List.sort points
        
        Assert.Equal(4, sorted.Length)
        Assert.Equal(latticePoint.create [|1; 3|], sorted.[0])
        Assert.Equal(latticePoint.create [|1; 5|], sorted.[1])
        Assert.Equal(latticePoint.create [|2; 1|], sorted.[2])
        Assert.Equal(latticePoint.create [|3; 1|], sorted.[3])
    
    [<Fact>]
    let ``isNonDecreasing detects non-decreasing sequences`` () =
        let p1 = latticePoint.create [|1; 2; 3; 3; 4|]
        let p2 = latticePoint.create [|1; 2; 1|]
        let p3 = latticePoint.create [|5|]
        
        Assert.True(LatticePoint.isNonDecreasing p1)
        Assert.False(LatticePoint.isNonDecreasing p2)
        Assert.True(LatticePoint.isNonDecreasing p3)
    
    [<Fact>]
    let ``Sum property calculates correctly`` () =
        let p = latticePoint.create [|1; 2; 3; 4|]
        
        Assert.Equal(10, p.Sum)
    
    [<Fact>]
    let ``Dimension property returns correct length`` () =
        let p1 = latticePoint.create [|1; 2; 3|]
        let p2 = latticePoint.create [|1|]
        
        Assert.Equal(3, p1.Dimension)
        Assert.Equal(1, p2.Dimension)
    
    [<Fact>]
    let ``Item indexer works correctly`` () =
        let p = latticePoint.create [|10; 20; 30|]
        
        Assert.Equal(10, p.[0])
        Assert.Equal(20, p.[1])
        Assert.Equal(30, p.[2])
    
    [<Fact>]
    let ``Sorting with negative numbers works`` () =
        let points = [|
            latticePoint.create [|-1; 2|]
            latticePoint.create [|-2; 1|]
            latticePoint.create [|0; 0|]
            latticePoint.create [|-1; 1|]
        |]
        
        let sorted = Array.sort points
        
        Assert.Equal(latticePoint.create [|-2; 1|], sorted.[0])
        Assert.Equal(latticePoint.create [|-1; 1|], sorted.[1])
        Assert.Equal(latticePoint.create [|-1; 2|], sorted.[2])
        Assert.Equal(latticePoint.create [|0; 0|], sorted.[3])
    
    [<Fact>]
    let ``Sorting maintains stability for equal elements`` () =
        let p1 = latticePoint.create [|1; 2|]
        let p2 = latticePoint.create [|1; 2|]  // Equal to p1
        
        let result = (p1 :> System.IComparable).CompareTo(p2)
        
        Assert.Equal(0, result)
    
    [<Theory>]
    [<InlineData(1, 2, -1)>]
    [<InlineData(2, 1, 1)>]
    [<InlineData(5, 5, 0)>]
    let ``Single element comparison`` (a: int) (b: int) (expected: int) =
        let p1 = latticePoint.create [|a|]
        let p2 = latticePoint.create [|b|]
        
        let result = (p1 :> System.IComparable).CompareTo(p2)
        
        Assert.Equal(expected, sign result)
    
    [<Fact>]
    let ``Empty arrays are equal`` () =
        let p1 = latticePoint.create [||]
        let p2 = latticePoint.create [||]
        
        Assert.Equal(p1, p2)
        let result = (p1 :> System.IComparable).CompareTo(p2)
        Assert.Equal(0, result)
    
    [<Fact>]
    let ``Array.sortDescending works`` () =
        let points = [|
            latticePoint.create [|1; 2|]
            latticePoint.create [|3; 1|]
            latticePoint.create [|2; 1|]
        |]
        
        let sorted = Array.sortDescending points
        
        Assert.Equal(latticePoint.create [|3; 1|], sorted.[0])
        Assert.Equal(latticePoint.create [|2; 1|], sorted.[1])
        Assert.Equal(latticePoint.create [|1; 2|], sorted.[2])






































    //----------------------------------------------------------------------
    // getPermutationIndex tests
    //----------------------------------------------------------------------

    //[<Fact>]
    //let ``getPermutationIndex equals sum(lowPoint) when single +1 change`` () =
    //    let a = lp [|1; 0; 2|]
    //    let b = lp [|1; 1; 2|]   // +1 at index 1

    //    let result = getPermutationIndex a b

    //    // sum(a) = 1 + 0 + 2 = 3
    //    Assert.Equal(3, result)


    //[<Fact>]
    //let ``getPermutationIndex propagates getOverIndex exceptions`` () =
    //    let a = lp [|1; 1; 1|]
    //    let b = lp [|3; 1; 1|]    // illegal change

    //    let ex =
    //        Assert.Throws<System.ArgumentException>(fun () ->
    //            getPermutationIndex a b |> ignore)

    //    Assert.Contains("not equal to +1", ex.Message)