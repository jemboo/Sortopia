namespace Sortopia.Sorting.Tests

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks

type SortableIntArrayTests() =

    [<Fact>]
    let ``Create with valid inputs succeeds`` () =
        let arr = sortableIntArray.create([| 0; 2; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        Assert.Equal<int>([| 0; 2; 1 |], arr.Values)
        Assert.Equal(3<channelCount>, arr.ChannelCount)
        Assert.Equal(3, %arr.SymbolSetSize)




    [<Fact>]
    let ``IsSorted returns true for sorted array`` () =
        let arr = sortableIntArray.create([| 0; 1; 2 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        Assert.True((arr.IsSorted))

    [<Fact>]
    let ``IsSorted returns false for unsorted array`` () =
        let arr = sortableIntArray.create([| 0; 2; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        Assert.False(arr.IsSorted)

    [<Fact>]
    let ``IsSorted returns true for single element`` () =
        let arr = sortableIntArray.create([| 0 |], 1<channelCount>, (1 |> UMX.tag<symbolSetSize>))
        Assert.True((arr.IsSorted))

    [<Fact>]
    let ``IsSorted returns true for empty array`` () =
        let arr = sortableIntArray.create([||], 0<channelCount>, (1 |> UMX.tag<symbolSetSize>))
        Assert.True((arr.IsSorted))





    [<Fact>]
    let ``ToSortableBoolArrays with ChannelCount 0 returns empty array`` () =
        let arr = sortableIntArray.create([||], 0<channelCount>, (1 |> UMX.tag<symbolSetSize>))
        let boolArrays = arr.ToSortableBoolArrays()
        Assert.Equal(0, boolArrays.Length)

    [<Fact>]
    let ``ToSortableBoolArrays with ChannelCount 1 returns empty array`` () =
        let arr = sortableIntArray.create([| 0 |], 1<channelCount>, (1 |> UMX.tag<symbolSetSize>))
        let boolArrays = arr.ToSortableBoolArrays()
        Assert.Equal(0, boolArrays.Length)


    [<Fact>]
    let ``Equality with same values returns true`` () =
        let arr1 = sortableIntArray.create([| 0; 2; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        let arr2 = sortableIntArray.create([| 0; 2; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        Assert.True((arr1.Equals(arr2)))
        Assert.True((arr1.Equals(arr2 :> obj)))
        Assert.Equal(arr1.GetHashCode(), arr2.GetHashCode())

    [<Fact>]
    let ``Equality with different values returns false`` () =
        let arr1 = sortableIntArray.create([| 0; 2; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        let arr2 = sortableIntArray.create([| 1; 0; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        Assert.False(arr1.Equals(arr2))
        Assert.False(arr1.Equals(arr2 :> obj))

    [<Fact>]
    let ``Equality with different channelCount returns false`` () =
        let arr1 = sortableIntArray.create([| 0; 2 |], 2<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        let arr2 = sortableIntArray.create([| 0; 2; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        Assert.False(arr1.Equals(arr2))
        Assert.False(arr1.Equals(arr2 :> obj))

    [<Fact>]
    let ``Equality with different type returns false`` () =
        let arr = sortableIntArray.create([| 0; 2; 1 |], 3<channelCount>, (3 |> UMX.tag<symbolSetSize>))
        let obj = obj()
        Assert.False(arr.Equals (obj))


    [<Fact>]
    let ``compare bool and int merged sorterTests`` () =
        
        let sortableBoolArrays = SortableBoolArray.getMergeTestCases
                                    6<channelCount>
                                    3<mergeDimension>
                                    mergeSuffixType.NoSuffix


        let sortableIntArrays = SortableIntArray.getMergeTestCases
                                    6<channelCount>
                                    3<mergeDimension>
                                    mergeSuffixType.NoSuffix

        let boolConv = 
            sortableIntArrays 
            |> Array.map (fun sia -> 
                sia.ToSortableBoolArrays()
            ) |> Array.concat |> SortableBoolArray.removeDuplicates

        let mutable report = ""
        boolConv |> Seq.iter (fun sba ->
            report <- report + sprintf "%A\n" sba.Values
        )

        Assert.Contains("M", "ex.Message")