namespace Sortopia.Core

open System
open System.Collections.Generic

/// Defines strategy variations for integer sampling across distinct distribution curves.
type intSampleMethod =
    | Constant of stepSize: int
    | Exponential of growthRate: float * firstDelta: int
    | Cyclic of baseMethod: intSampleMethod * cycleLength: int
    | Prefixed of prefixLength: int * prefixMethod: intSampleMethod * mainMethod: intSampleMethod

type genIntervalConfig = {
    Name: string
    Min: int
    MaxCount: int option
    Scale: float
    Method: intSampleMethod
}

module IntSampleMethod =

    /// Serializes an IntSampleMethod into a string representation.
    let rec toString (method: intSampleMethod) : string =
        match method with
        | Constant step -> 
            sprintf "Constant(%d)" step
        | Exponential (rate, firstDelta) -> 
            sprintf "Exponential(%f, %d)" rate firstDelta
        | Cyclic (baseMethod, cycleLen) -> 
            sprintf "Cyclic(%s, %d)" (toString baseMethod) cycleLen
        | Prefixed (prefixLen, prefixMethod, mainMethod) -> 
            sprintf "Prefixed(%d, %s, %s)" prefixLen (toString prefixMethod) (toString mainMethod)

    /// Parses a string representation back into an IntSampleMethod.
    let rec fromString (s: string) : intSampleMethod =
        let trimmed = s.Trim()
        
        let splitTopLevelArgs (str: string) =
            let mutable depth = 0
            let mutable current = ""
            let args = ResizeArray<string>()
            for ch in str do
                match ch with
                | '(' -> 
                    depth <- depth + 1
                    current <- current + string ch
                | ')' -> 
                    depth <- depth - 1
                    current <- current + string ch
                | ',' when depth = 0 ->
                    args.Add(current.Trim())
                    current <- ""
                | _ -> 
                    current <- current + string ch
            if not (String.IsNullOrWhiteSpace current) then
                args.Add(current.Trim())
            args |> Seq.toList

        if trimmed.StartsWith("Constant(") && trimmed.EndsWith(")") then
            let inner = trimmed.Substring(9, trimmed.Length - 10)
            Constant (int (inner.Trim()))

        elif (trimmed.StartsWith("Exponential(") || trimmed.StartsWith("ExponentialDecrease(")) && trimmed.EndsWith(")") then
            let prefixLen = if trimmed.StartsWith("ExponentialDecrease(") then 20 else 12
            let inner = trimmed.Substring(prefixLen, trimmed.Length - (prefixLen + 1))
            let args = splitTopLevelArgs inner
            if args.Length = 1 then
                Exponential (float args.[0], 1)
            elif args.Length = 2 then
                Exponential (float args.[0], int args.[1])
            else
                invalidArg "s" "Exponential must have 1 or 2 arguments: growthRate, [firstDelta]."

        elif trimmed.StartsWith("Cyclic(") && trimmed.EndsWith(")") then
            let inner = trimmed.Substring(7, trimmed.Length - 8)
            let args = splitTopLevelArgs inner
            if args.Length <> 2 then
                invalidArg "s" "Cyclic must have 2 arguments: baseMethod, cycleLength."
            let baseMethod = fromString args.[0]
            let cycleLen = int args.[1]
            Cyclic (baseMethod, cycleLen)

        elif trimmed.StartsWith("Prefixed(") && trimmed.EndsWith(")") then
            let inner = trimmed.Substring(9, trimmed.Length - 10)
            let args = splitTopLevelArgs inner
            if args.Length <> 3 then
                invalidArg "s" "Prefixed must have 3 arguments: prefixLength, prefixMethod, mainMethod."
            let prefixLen = int args.[0]
            let prefixMethod = fromString args.[1]
            let mainMethod = fromString args.[2]
            Prefixed (prefixLen, prefixMethod, mainMethod)

        else
            invalidArg "s" (sprintf "Unrecognized IntSampleMethod format: %s" s)

    /// Unbounded lazy sequence generator for a sampling method starting at minVal.
    let rec generateUnbounded (method: intSampleMethod) (minVal: int) : int seq =
        seq {
            match method with
            | Constant step ->
                let stepSize = max 1 step
                let mutable current = minVal
                while true do
                    yield current
                    current <- current + stepSize

            | Exponential (rate, firstDelta) ->
                let r = if rate <= 0.0 then 0.05 else rate
                let mutable current = float minVal
                let mutable step = float (max 1 firstDelta)
                while true do
                    yield int (round current)
                    current <- current + step
                    step <- step * (1.0 + r)

            | Cyclic (baseMethod, cycleLen) ->
                let len = max 1 cycleLen
                let mutable currentStart = minVal
                while true do
                    let samples = generateUnbounded baseMethod currentStart |> Seq.take len |> Seq.toList
                    yield! samples
                    if not (List.isEmpty samples) then
                        currentStart <- List.last samples + 1

            | Prefixed (prefixLen, prefixMethod, mainMethod) ->
                let pLen = max 0 prefixLen
                if pLen = 0 then
                    yield! generateUnbounded mainMethod minVal
                else
                    let prefixSamples = generateUnbounded prefixMethod minVal |> Seq.take pLen |> Seq.toList
                    yield! prefixSamples
                    let nextStart = 
                        if List.isEmpty prefixSamples then minVal 
                        else List.last prefixSamples + 1
                    yield! generateUnbounded mainMethod nextStart
        }

    /// Generates sequence values given a sampling method, start value, and count limit.
    let generate (method: intSampleMethod) (minVal: int) (count: int) : int seq =
        if count <= 0 then Seq.empty
        else generateUnbounded method minVal |> Seq.take count


module GenIntervalConfig =

    let toString (config: genIntervalConfig) : string =
        sprintf "Name: %s, Min: %d, MaxCount: %s, Scale: %f, Method: %s" 
            config.Name 
            config.Min 
            (match config.MaxCount with | Some c -> string c | None -> "None") 
            config.Scale 
            (IntSampleMethod.toString config.Method)

    let fromString (s: string) : genIntervalConfig =
        let parts = s.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
        if parts.Length <> 5 then
            invalidArg "s" "Invalid genIntervalConfig string format. Expected 5 comma-separated values."
        let name = parts.[0].Trim().Substring(6).Trim()
        let min = int (parts.[1].Trim().Substring(5).Trim())
        let maxCountStr = parts.[2].Trim().Substring(9).Trim()
        let maxCount = if maxCountStr = "None" then None else Some (int maxCountStr)
        let scale = float (parts.[3].Trim().Substring(7).Trim())
        let methodStr = parts.[4].Trim().Substring(7).Trim()
        let method = IntSampleMethod.fromString methodStr
        { Name = name; Min = min; MaxCount = maxCount; Scale = scale; Method = method }

    /// Returns the zero-based sample at the requested index after applying the config scale.
    /// Returns None when the index is negative or beyond MaxCount.
    let getSampleAtIndex (config: genIntervalConfig) (index: int) : int option =
        if index < 0 then
            None
        else
            let rawSeq = IntSampleMethod.generateUnbounded config.Method config.Min
            let boundedSeq =
                match config.MaxCount with
                | Some maxCount -> rawSeq |> Seq.take (max 0 maxCount)
                | None -> rawSeq

            boundedSeq
            |> Seq.map (fun sample -> int (ceil (float sample * config.Scale)))
            |> Seq.tryItem index


    /// Evaluates the method to yield a scaled set of integer samples constrained by maxBound.
    let getSampleSetMaxBound (config: genIntervalConfig) (maxBound: int) : Set<int> =
        let rawSeq = IntSampleMethod.generateUnbounded config.Method config.Min
        
        let boundedSeq = 
            match config.MaxCount with
            | Some maxCount -> rawSeq |> Seq.take maxCount
            | None -> rawSeq

        boundedSeq
        |> Seq.map (fun x -> int (ceil (float x * config.Scale)))
        |> Seq.takeWhile (fun scaledVal -> scaledVal <= maxBound)
        |> Set.ofSeq


    /// Evaluates the method to yield a scaled set of the first sampleCount integer samples strictly larger than minBound.
    let getSamplesWithMinBound (config: genIntervalConfig) (minBound: int) : seq<int> =
        let rawSeq = IntSampleMethod.generateUnbounded config.Method config.Min
            
        let boundedSeq = 
            match config.MaxCount with
            | Some maxCount -> rawSeq |> Seq.take maxCount
            | None -> rawSeq

        boundedSeq
        |> Seq.map (fun x -> int (ceil (float x * config.Scale)))
        |> Seq.filter (fun scaledVal -> scaledVal > minBound)
            

module GenIntervalNames =

    [<Literal>]
    let uniformInterval1 = "uniformInterval1"

    [<Literal>]
    let uniformInterval2 = "uniformInterval2"

    [<Literal>]
    let uniformInterval5 = "uniformInterval5"

    [<Literal>]
    let uniformInterval10 = "uniformInterval10"

    [<Literal>]
    let uniformInterval100 = "uniformInterval100"

    [<Literal>]
    let uniformInterval500 = "uniformInterval500"

    [<Literal>]
    let uniformInterval1000 = "uniformInterval1000"

    [<Literal>]
    let summaryInterval_C_1p5C = "summaryInterval_C.1p5C"

    [<Literal>]
    let summaryInterval_C_2C = "summaryInterval_C.2C"

    [<Literal>]
    let summaryInterval_C_5C = "summaryInterval_C.5C"

    [<Literal>]
    let summaryInterval_C_K = "summaryInterval_C.K"

    [<Literal>]
    let summaryInterval_C_5K = "summaryInterval_C.5K"

    [<Literal>]
    let emptyInterval = "emptyInterval"

    [<Literal>]
    let uniformInterval5_L5 = "uniformInterval5_L5"

    [<Literal>]
    let uniformInterval5_L10 = "uniformInterval5_L10"

    [<Literal>]
    let expInterval25_L5 = "expInterval25_L5"

    [<Literal>]
    let expInterval25_L20 = "expInterval25_L20"

    [<Literal>]
    let expInterval50_L10 = "expInterval50_L10"

    [<Literal>]
    let expInterval100_L10 = "expInterval100_L10"

    [<Literal>]
    let expInterval100_L50 = "expInterval100_L50"

    [<Literal>]
    let expInterval100_L50s = "expInterval100_L50s"

    [<Literal>]
    let expInterval100_L50ss = "expInterval100_L50ss"

    [<Literal>]
    let periodic_LinearBurst = "periodic_LinearBurst"

    [<Literal>]
    let periodic_DecayReset = "periodic_DecayReset"

    [<Literal>]
    let prefixed_PeriodicBurst = "prefixed_PeriodicBurst"

module GenIntervalRegistry =

    let private createConfig name min maxCount scale method = {
        Name = name
        Min = min
        MaxCount = maxCount
        Scale = scale
        Method = method
    }

    let scale1 = 1.0
    let scale2 = 2.0
    let scale5 = 5.0
    let scale10 = 10.0
    let scale100 = 100.0
    let scale500 = 500.0
    let scale1000 = 1000.0
    let noMaxCount = None
    let maxCount0 = Some 0
    let maxCount5 = Some 5
    let maxCount10 = Some 10
    let maxCount20 = Some 20
    let maxCount50 = Some 50

    let genIntervalConfigsDict: Dictionary<string, genIntervalConfig> = 

        let dict = Dictionary<string, genIntervalConfig>()
        let add cfg = dict.Add(cfg.Name, cfg)


        //uniformInterval2             : [2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40]
        //uniformInterval10            : [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200]
        //uniformInterval100           : [100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500, 1600, 1700, 1800, 1900, 2000]
        //uniformInterval500           : [500, 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500, 6000, 6500, 7000, 7500, 8000, 8500, 9000, 9500, 10000]
        //uniformInterval1000          : [1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000, 11000, 12000, 13000, 14000, 15000, 16000, 17000, 18000, 19000, 20000]
        
        add (createConfig GenIntervalNames.uniformInterval1 1 noMaxCount scale1 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval2 1 noMaxCount scale2 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval5 1 noMaxCount scale5 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval10 1 noMaxCount scale10 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval100 1 noMaxCount scale100 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval500 1 noMaxCount scale500 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval1000 1 noMaxCount scale1000 (Constant 1))


       // summaryInterval_C.1p5C       : [1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 16, 17, 18, 20, 21, 22, 24, 25, 27, 28, 30, 31, 33, 35, 36, 38, 40, 42, 
       //                                 43, 45, 47, 49, 51, 53, 55, 57, 59, 61, 64, 66, 68, 71, 73, 75, 78, 80, 83, 86, 88, 91, 94, 97, 100, 103, 106, 109, 
       //                                 112, 115, 118, 122, 125, 129, 132, 136, 139, 143, 147, 151, 155, 159, 163, 167, 172, 176, 181, 185, 190, 195, 200, 205, 
       //                                 210, 215, 220, 226, 231, 237, 242, 248, 254, 260, 266, 273, 279, 286, 292, 299, 306, 313, 320, 328, 335, 343, 351, 359, 
       //                                 367, 375, 384, 393, 401, 410, 420, 429, 439, 448, 458, 468, 479, 489, 500, 511, 522, 534, 545, 557, 569, 582, 594, 607, 
       //                                 620, 634, 647, 661, 675, 690, 705, 720, 735, 751, 767, 783, 800, 817, 834, 852, 870, 888, 907, 926, 945, 965, 986, 1006, 1027, 1049, 1071, 1093, 1116, 1139, 1163, 1188, 1212, 1237, 1263, 1289, 1316, 1344, 1371, 1400, 1429, 1458, 1488, 1519, 1551, 1583, 1615, 1648, 1682, 1717, 1752, 1788, 1825, 1863, 1901, 1940, 1980, 2020, 2062, 2104, 2147, 2191, 2236, 2281, 2328, 2375, 2424, 2473, 2524, 2575, 2628, 2681, 2736, 2792, 2848, 2906, 2965, 3026, 3087, 3150, 3214, 3279, 3346, 3414, 3483, 3554, 3626, 3699, 3774, 3850, 3928, 4008, 4089, 4172, 4256, 4342, 4430, 4520, 4611, 4704, 4800, 4896, 4995, 5096, 5199, 5304, 5411, 5520, 5632, 5745, 5861, 5980, 6100, 6223, 6349, 6476, 6607, 6740, 6876, 7014, 7156, 7300, 7447, 7597, 7750, 7906, 8065, 8227, 8392, 8561, 8733, 8909, 9088, 9271, 9457, 9648, 9841, 10039, 10241, 10447, 10657, 10871, 11089, 11312, 11539, 11771, 12007, 12249, 12494, 12745, 13001, 13262, 13528, 13800, 14077, 14360, 14648, 14942, 15241, 15547, 15859, 16177, 16502, 16833, 17171, 17515, 17866, 18224, 18590, 18963, 19343, 19731, 20126, 20530, 20941, 21361, 21789, 22226, 22672, 23126, 23590, 24062, 24545, 25037, 25538, 26050, 26572, 27104, 27647, 28201, 28766, 29343, 29931, 30530, 31142, 31766, 32402, 33051, 33713, 34388, 35077, 35779, 36496, 37227, 37972, 38733, 39508, 40300, 41107, 41930, 42769, 43626, 44499, 45390, 46299, 47226, 48171, 49136, 50119, 51123, 52146, 53190, 54255, 55341, 56449, 57579, 58731, 59907, 61106, 62329, 63577, 64849, 66147, 67471, 68821, 70199, 71604, 73037, 74499, 75990, 77510, 79061, 80644, 82258, 83904, 85583, 87295, 89042, 90824, 92642, 94495, 96386, 98315]

        //summaryInterval_C.2C         : [1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 14, 15, 17, 18, 20, 21, 23, 24, 26, 28, 30, 32, 33, 35, 37, 40, 42, 44, 46, 49, 
        //                                51, 54, 56, 59, 61, 64, 67, 70, 73, 76, 80, 83, 86, 90, 94, 98, 101, 105, 110, 114, 118, 123, 127, 132, 137, 142, 147, 
        //                                153, 158, 164, 170, 176, 182, 189, 195, 202, 209, 216, 224, 232, 240, 248, 256, 265, 274, 283, 292, 302, 312, 322, 333, 
        //                                344, 355, 367, 379, 391, 404, 417, 430, 444, 459, 473, 489, 504, 520, 537, 554, 572, 590, 608, 628, 647, 668, 689, 710, 
        //                                733, 756, 779, 804, 829, 854, 881, 908, 937, 966, 996, 1027, 1058, 1091, 1125, 1159, 1195, 1232, 1270, 1309, 1349, 1391, 
        //                                1433, 1477, 1523, 1569, 1617, 1667, 1718, 1770, 1824, 1880, 1937, 1997, 2057, 2120, 2185, 2251, 2320, 2390, 2463, 2538, 
        //                                2615, 2694, 2776, 2860, 2947, 3037, 3129, 3223, 3321, 3422, 3525, 3632, 3742, 3855, 3972, 4092, 4216, 4343, 4474, 4610, 
        //                                4749, 4892, 5040, 5192, 5349, 5510, 5677, 5848, 6024, 6206, 6393, 6586, 6784, 6989, 7200, 7417, 7640, 7870, 8107, 8351, 
        //                                8603, 8862, 9129, 9404, 9687, 9978, 10279, 10588, 10907, 11235, 11573, 11921, 12280, 12649, 13029, 13421, 13825, 14240, 
        //                                14669, 15110, 15564, 16032, 16514, 17010, 17521, 18048, 18590, 19149, 19725, 20317, 20928]
        //summaryInterval_C.5C         : [1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 13, 15, 16, 18, 19, 21, 23, 25, 27, 29, 31, 33, 36, 38, 41, 43, 46, 49, 52, 55]
        //summaryInterval_C.K          : [1, 2, 3, 4, 5, 7, 8, 9, 11, 12, 14, 15, 17, 19, 21, 23, 25, 27, 29, 32, 34, 37, 40, 42, 46, 49, 52, 56, 59, 63]
        //summaryInterval_C.5K         : [1, 2, 3, 4, 5, 7, 8, 10, 11, 13, 15, 17, 19, 21, 24, 26, 29, 32, 35, 38, 42, 46, 50, 54, 59, 64, 70, 75, 82, 88]
        add (createConfig GenIntervalNames.summaryInterval_C_1p5C 1 noMaxCount scale1 (Exponential (0.02, 1)))
        add (createConfig GenIntervalNames.summaryInterval_C_2C 1 noMaxCount scale1 (Exponential (0.03, 1)))
        add (createConfig GenIntervalNames.summaryInterval_C_5C 1 noMaxCount scale1 (Exponential (0.041, 1)))
        add (createConfig GenIntervalNames.summaryInterval_C_K 1 noMaxCount scale1 (Exponential (0.05, 1)))
        add (createConfig GenIntervalNames.summaryInterval_C_5K 1 noMaxCount scale1 (Exponential (0.07, 1)))

        //emptyInterval                : [<empty>]
        //uniformInterval5_L5          : [5, 10, 15, 20, 25]
        //uniformInterval5_L10         : [5, 10, 15, 20, 25, 30, 35, 40, 45, 50]
        add (createConfig GenIntervalNames.emptyInterval 0 maxCount0 scale1 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval5_L5 1 maxCount5 scale5 (Constant 1))
        add (createConfig GenIntervalNames.uniformInterval5_L10 1 maxCount10 scale5 (Constant 1))

        //expInterval25_L5             : [25, 65, 125, 215, 350]
        //expInterval25_L20            : [25, 55, 94, 145, 211, 296, 408, 552, 741, 985, 1304, 1717, 2255, 2954, 3862, 5044, 6579, 8575, 11171, 14544]
        //expInterval50_L10            : [50, 125, 238, 406, 659, 1039, 1609, 2463, 3744, 5667]
        //expInterval100_L10           : [100, 250, 475, 812, 1319, 2078, 3217, 4926, 7489, 11333]
        //expInterval100_L50           : [100, 250, 475, 812, 1319, 2078, 3217, 4926, 7489, 11333, 17100, 25749, 38724, 58186, 87379]
        //expInterval100_L50s          : [100, 250, 445, 698, 1028, 1456, 2013, 2737, 3679, 4902, 6493, 8561, 11249, 14744, 19287, 25193, 32871, 42852, 55828, 72696, 94625]
        //expInterval100_L50ss         : [100, 250, 430, 646,  905, 1216, 1589, 2037, 2575, 3220, 3994, 4923, 6037, 7374, 8979, 10905, 13216, 15990, 19317, 23311, 28103, 33854, 
        //                                40755, 49036, 58973, 70897, 85207]
        
        add (createConfig GenIntervalNames.expInterval25_L5 25 maxCount5 scale1 (Exponential (0.5, 40)))
        add (createConfig GenIntervalNames.expInterval25_L20 25 maxCount20 scale1 (Exponential (0.3, 30)))
        add (createConfig GenIntervalNames.expInterval50_L10 50 maxCount10 scale1 (Exponential (0.5, 75)))
        add (createConfig GenIntervalNames.expInterval100_L10 100 maxCount10 scale1 (Exponential (0.5, 150)))
        add (createConfig GenIntervalNames.expInterval100_L50 100 maxCount50 scale1 (Exponential (0.5, 150)))
        add (createConfig GenIntervalNames.expInterval100_L50s 100 maxCount50 scale1 (Exponential (0.3, 150)))
        add (createConfig GenIntervalNames.expInterval100_L50ss 100 maxCount50 scale1 (Exponential (0.2, 150)))

        //periodic_LinearBurst         : [1, 3, 5, 7, 9, 10, 12, 14, 16, 18, 19, 21, 23, 25, 27, 28, 30, 32, 34, 36]
        //periodic_DecayReset          : [5, 10, 15, 25, 30, 40, 45, 50, 55, 65, 70, 80, 85, 90, 95, 105, 110, 120, 125, 130]
        //prefixed_PeriodicBurst       : [1, 11, 21, 31, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56]
        add (createConfig GenIntervalNames.periodic_LinearBurst 1 maxCount50 scale1 (Cyclic (Constant 2, 5)))
        add (createConfig GenIntervalNames.periodic_DecayReset 1 maxCount50 scale5 (Cyclic (Exponential (0.2, 1), 6)))
        add (createConfig GenIntervalNames.prefixed_PeriodicBurst 1 maxCount50 scale1 (Prefixed (5, Constant 10, Cyclic (Constant 1, 5))))

        dict
