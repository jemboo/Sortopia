namespace Sortopia.Sorting.Tests

open System
open FSharp.UMX
open Xunit
open Sortopia.Sorting.Tests.Samples
open Sortopia.Sorting
open Sortopia.Sorting.Networks

type SorterSetTests() =

    [<Fact>]
    let ``SorterSet.create creates valid SorterSet``() =
        let createSorter ces =
            newSorter (UMX.tag<channelCount> 4) ces
        
        let ces1 = [| ce.create 0 1; ce.create 1 1 |]
        let ces2 = [| ce.create 0 2; ce.create 1 2 |]
        let sorters = [| createSorter ces1; createSorter ces2 |]
        
        let sorterSet = newSorterSet sorters
        Assert.NotEqual(Guid.Empty, %sorterSet.Id)
        Assert.Equal(2, (%sorterSet.Sorters.Length * 1<sorterCount>))
        Assert.Equal(ces1 |> Array.toList, sorterSet.Sorters.[0].Ces)
        Assert.Equal(ces2 |> Array.toList, sorterSet.Sorters.[1].Ces)
        Assert.Equal(4, %sorterSet.Sorters.[0].ChannelCount)
        Assert.Equal(4, %sorterSet.Sorters.[1].ChannelCount)
