namespace Sortopia.Reporting

open Sortopia.Core

type dataTableRecord = 
    private 
        { keys: Map<string, string>
          data: Map<string, string> }
    
    member this.Keys with get() = this.keys
    member this.Data with get() = this.data
    
    static member createEmpty() : dataTableRecord =
        { keys = Map.empty; data = Map.empty }

    static member create ( keys: Map<string, string> ) 
                         ( data: Map<string, string> ) : dataTableRecord =
        { keys = keys; data = data }

    /// Helper to sanitize row data to prevent column shifting
    static member addKeyAndData (key: string) (value: string) (dataTableRecord: dataTableRecord) : dataTableRecord =
        let cleanedKey = if key = null then "" else key.Replace("\t", " ")
        let cleanedValue = if value = null then "" else value.Replace("\t", " ")
        { keys = dataTableRecord.Keys.Add(cleanedKey, cleanedValue); data = dataTableRecord.Data.Add(cleanedKey, cleanedValue) }

    static member addData (key: string) (value: string) (dataTableRecord: dataTableRecord) : dataTableRecord =
        let cleanedKey = if key = null then "" else key.Replace("\t", " ")
        let cleanedValue = if value = null then "" else value.Replace("\t", " ")
        { keys = dataTableRecord.Keys; data = dataTableRecord.Data.Add(cleanedKey, cleanedValue) }

    // Merges keys and data from both.
    static member combine (record1: dataTableRecord) (record2: dataTableRecord) : dataTableRecord =
        let mergedKeys = record1.Keys |> Map.fold (fun (acc: Map<string,string>) k v -> acc.Add(k, v)) record2.Keys
        let mergedData = record1.Data |> Map.fold (fun (acc: Map<string,string>) k v -> acc.Add(k, v)) record2.Data
        { keys = mergedKeys; data = mergedData }

    //// Combines a root record with all records, merging keys and data from both.
    static member combineWithMany (records: dataTableRecord seq) (rootRecord: dataTableRecord)  : dataTableRecord seq =
        records |> Seq.map (fun r -> dataTableRecord.combine rootRecord r)

    static member createWithKeyAndData (key: string) (value: string) : dataTableRecord =
        let cleanedKey = if key = null then "" else key.Replace("\t", " ")
        let cleanedValue = if value = null then "" else value.Replace("\t", " ")
        { keys = Map.ofList [(cleanedKey, cleanedValue)]; data = Map.ofList [(cleanedKey, cleanedValue)] }

    static member createTable (records: dataTableRecord seq) : (string []) * (string [][]) =
        let allKeys = records |> Seq.collect (fun r -> r.Data |> Map.toSeq) |> Seq.map fst |> Seq.distinct
        let header = allKeys |> Seq.toArray
        let rows = 
            records 
            |> Seq.map (fun r -> 
                header 
                |> Array.map (fun k -> if r.Data.ContainsKey(k) then r.Data.[k] else ""))
            |> Seq.toArray
        (header, rows)


open System.IO
