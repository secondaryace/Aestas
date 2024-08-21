namespace Aestas
open System
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Collections.Generic
open System.Text.Encodings.Web
open System.Text.Unicode
open System.Net.Http

module Prim = 
    let version = Version(0, 240817)
    #if DEBUG
    let debug = true
    #else
    let debug = false
    #endif
    let ( >.|> ) f g a b =  g b (f a b)
    let ( >..|> ) f g a b = g (f a b) b
    let inline is<'t when 't: not struct> (a: obj) =
        match a with
        | :? 't -> true
        | _ -> false
    let inline await a = a |> Async.AwaitTask |> Async.RunSynchronously
    let inline isNotNull a = a |> isNull |> not
    type ICombinatedCtor<'t, 'tArg> =
        static abstract member Make: 'tArg -> 't
    let inline make<'t, 'tArg when 't :> ICombinatedCtor<'t, 'tArg>> arg = 't.Make arg
    let inline fst' struct(a, b) = a
    let inline snd' struct(a, b) = b
    let inline tryWrap (f: unit -> 't) = try f() |> Some with _ -> None
    let inline toString (o: obj) =
        match o with
        | :? int as i -> i.ToString()
        | :? float as f -> f.ToString()
        | :? single as f -> f.ToString()
        | :? bool as b -> b.ToString()
        | :? char as c -> c.ToString()
        | :? byte as b -> b.ToString()
        | :? sbyte as b -> b.ToString()
        | :? string as s -> s
        | :? Type as t -> t.Name
        | _ -> o.GetType().Name
    let fsOptions = 
        JsonFSharpOptions.Default()
            .WithUnionUntagged()
            .WithUnionUnwrapRecordCases()
            .WithSkippableOptionFields(SkippableOptionFields.Always, true)
            .ToJsonSerializerOptions()
    do
        fsOptions.Encoder <- JavaScriptEncoder.Create(UnicodeRanges.All)
    let inline jsonDeserialize<'t> (x: string) = 
        JsonSerializer.Deserialize<'t>(x, fsOptions)
    let inline jsonSerialize (x: 't) = 
        JsonSerializer.Serialize<'t>(x, fsOptions)
    let inline (|StrStartWith|_|) (s: string) (x: string) = 
        if x.StartsWith(s) then Some x else None
    let inline implInterface<'t> (x: Type) = x.GetInterfaces() |> Array.contains typeof<'t> 
    type IndexerBox<'tArg, 't>([<InlineIfLambda>]func: 'tArg -> 't) = 
        member _.Item with get(arg: 'tArg) = func arg
    type 't arrList = ResizeArray<'t>
    type Collections.Generic.List<'t> with
        member inline this.GetReverseIndex(_:int, offset) = this.Count-offset-1
        member inline this.GetSlice(startIndex: int option, endIndex: int option) = 
            match startIndex, endIndex with
            | None, None -> this
            | Some i, None -> this.Slice(i, this.Count-i)
            | Some i, Some j -> this.Slice(i, j-i)
            | None, Some j -> this.Slice(0, j)
    module Itor =
        let inline moveNext (itor: IEnumerator<'t>) = itor.MoveNext()
        let inline current (itor: IEnumerator<'t>) = itor.Current
        let inline ofEnumerable (e: IEnumerable<'t>) = e.GetEnumerator()
    module ArrList =
        let inline map (mapping: 't -> 'u) (list: arrList<'t>) =
            let result = arrList<'u>()
            let rec go i =
                if i >= list.Count then result
                else
                    list[i] |> mapping |> result.Add
                    go (i+1)
            go 0 
        let inline iter (action: 't -> unit) (list: arrList<'t>) =
            let rec go i =
                if i >= list.Count then ()
                else
                    list[i] |> action
                    go (i+1)
            go 0
        let inline fold (folder: 'state -> 't -> 'state) (state: 'state) (list: arrList<'t>) =
            let rec go i state =
                if i >= list.Count then state
                else
                    go (i+1) (folder state list[i])
            go 0 state
        let inline singleton (value: 't) = 
            let result = arrList<'t>()
            result.Add value
            result
        let inline tryFind (predicate: 't -> bool) (list: arrList<'t>) =
            let rec go i =
                if i >= list.Count then None
                elif predicate list[i] then Some list[i]
                else go (i+1)
            go 0
        let inline tryFindBack (predicate: 't -> bool) (list: arrList<'t>) =
            let rec go i =
                if i < 0 then None
                elif predicate list[i] then Some list[i]
                else go (i-1)
            go (list.Count-1)
        let inline tryFindIndex (predicate: 't -> bool) (list: arrList<'t>) =
            let rec go i =
                if i >= list.Count then None
                elif predicate list[i] then Some i
                else go (i+1)
            go 0
        let inline tryFindIndexBack (predicate: 't -> bool) (list: arrList<'t>) =
            let rec go i =
                if i < 0 then None
                elif predicate list[i] then Some i
                else go (i-1)
            go (list.Count-1)
        let inline find (predicate: 't -> bool) (list: arrList<'t>) =
            match list |> tryFind predicate with
            | Some x -> x
            | None -> failwith "Not found"
        let inline findBack (predicate: 't -> bool) (list: arrList<'t>) =
            match list |> tryFindBack predicate with
            | Some x -> x
            | None -> failwith "Not found"
    module IListExt =
        let inline map (mapping: 't -> 'u) (list: IList<'t>) =
            let result = arrList<'u>()
            let rec go i =
                if i >= list.Count then result
                else
                    list[i] |> mapping |> result.Add
                    go (i+1)
            go 0 
        let inline iter (action: 't -> unit) (list: IList<'t>) =
            let rec go i =
                if i >= list.Count then ()
                else
                    list[i] |> action
                    go (i+1)
            go 0
        let inline fold (folder: 'state -> 't -> 'state) (state: 'state) (list: IList<'t>) =
            let rec go i state =
                if i >= list.Count then state
                else
                    go (i+1) (folder state list[i])
            go 0 state
        let inline tryFind (predicate: 't -> bool) (list: IList<'t>) =
            let rec go i =
                if i >= list.Count then None
                elif predicate list[i] then Some list[i]
                else go (i+1)
            go 0
        let inline tryFindBack (predicate: 't -> bool) (list: IList<'t>) =
            let rec go i =
                if i < 0 then None
                elif predicate list[i] then Some list[i]
                else go (i-1)
            go (list.Count-1)
        let inline tryFindIndex (predicate: 't -> bool) (list: IList<'t>) =
            let rec go i =
                if i >= list.Count then None
                elif predicate list[i] then Some i
                else go (i+1)
            go 0
        let inline tryFindIndexBack (predicate: 't -> bool) (list: IList<'t>) =
            let rec go i =
                if i < 0 then None
                elif predicate list[i] then Some i
                else go (i-1)
            go (list.Count-1)
        let inline find (predicate: 't -> bool) (list: IList<'t>) =
            match list |> tryFind predicate with
            | Some x -> x
            | None -> failwith "Not found"
        let inline findBack (predicate: 't -> bool) (list: IList<'t>) =
            match list |> tryFindBack predicate with
            | Some x -> x
            | None -> failwith "Not found"
    module Dict =
        let inline deconstructPair (p: KeyValuePair<'k, 'v>) = p.Deconstruct()
        let inline iter (action: 'k -> 'v -> unit) (dict: IReadOnlyDictionary<'k, 'v>) =
            let rec go itor =
                if Itor.moveNext itor then
                    let k, v = deconstructPair itor.Current
                    action k v
                    go itor
            dict |> Itor.ofEnumerable |> go
        let inline addRange(this: Dictionary<'k, 'v>) (keys: 'k[]) (dict: IReadOnlyDictionary<'k, 'v>) =
            dict |> iter (fun k v -> if keys |> Array.contains k then this.Add(k, v))
        let inline addAll (this: Dictionary<'k, 'v>) (dict: IReadOnlyDictionary<'k, 'v>) =
            dict |> iter (fun k v -> this.Add(k, v))
        
    type Collections.Generic.Dictionary<'k, 'v> with
        member inline this.Append(d: IReadOnlyDictionary<'k, 'v>) =
            Dict.addAll this d
        
    let buildDatabasePrompt (system: string) (db: arrList<Dictionary<string, string>>) = 
        let sb = StringBuilder()
        sb.Append(system) |> ignore
        sb.Append("*Following is a dictionary for you:*") |> ignore
        for dic in db do
            for p in dic do
                sb.Append(';').Append(p.Key).Append(": ").Append(p.Value) |> ignore
        sb.ToString()
    let inline dict' a = a |> dict |> Dictionary
    let colorAt (arr: byte[]) w x y =
        let i = 4*(w*y+x)
        struct(arr[i], arr[i+1], arr[i+2], arr[i+3])
    let randomString (length: int) =
        let chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        let rand = Random()
        let sb = StringBuilder()
        for _ = 1 to length do
            sb.Append(chars[rand.Next(chars.Length)]) |> ignore
        sb.ToString()
    
    let bytesFromUrl (url: string) =
        try
        use web = new HttpClient()
        web.GetByteArrayAsync(url).Result
        with
        | _ -> 
            [|0uy|]
    let bash (cmd: string) =
        let psi = Diagnostics.ProcessStartInfo("/bin/bash", $"-c \"{cmd}\"")
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        use p = new Diagnostics.Process()
        p.StartInfo <- psi
        p.Start() |> ignore
        let result = p.StandardOutput.ReadToEnd()
        p.WaitForExit() |> ignore
        p.Kill()
        result
    let cmd (cmd: string) =
        let psi = Diagnostics.ProcessStartInfo("cmd", $"/c \"{cmd}\"")
        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        use p = new Diagnostics.Process()
        p.StartInfo <- psi
        p.Start() |> ignore
        let result = p.StandardOutput.ReadToEnd()
        p.WaitForExit() |> ignore
        p.Kill()
        result