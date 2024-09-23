namespace Aestas
    
    module Prim =
        
        val version: System.Version
        
        val debug: bool
        
        val (>.|>) :
          f: ('a -> 'b -> 'c) -> g: ('b -> 'c -> 'd) -> a: 'a -> b: 'b -> 'd
        
        val (>..|>) :
          f: ('a -> 'b -> 'c) -> g: ('c -> 'b -> 'd) -> a: 'a -> b: 'b -> 'd
        
        val inline curry: f: ('a * 'b -> 'c) -> a: 'a -> b: 'b -> 'c
        
        val inline curry3:
          f: ('a * 'b * 'c -> 'd) -> a: 'a -> b: 'b -> c: 'c -> 'd
        
        val inline curry4:
          f: ('a * 'b * 'c * 'd -> 'e) -> a: 'a -> b: 'b -> c: 'c -> d: 'd -> 'e
        
        val inline uncurry: f: ('a -> 'b -> 'c) -> a: 'a * b: 'b -> 'c
        
        val inline is<'t when 't: not struct> :
          a: obj -> bool when 't: not struct
        
        val inline await: a: System.Threading.Tasks.Task<'a> -> 'a
        
        val inline isNotNull: a: 'a -> bool when 'a: not struct
        
        type ICombinatedCtor<'t,'tArg> =
            
            static abstract Make: 'tArg -> 't
        
        val inline make: arg: 'tArg -> 't when 't :> ICombinatedCtor<'t,'tArg>
        
        val inline fst': struct ('a * 'b) -> 'a
        
        val inline snd': struct ('a * 'b) -> 'b
        
        val inline tryWrap: f: (unit -> 't) -> 't option
        
        val inline toString: o: obj -> string
        
        val fsOptions: System.Text.Json.JsonSerializerOptions
        
        val inline jsonDeserialize: x: string -> 't
        
        val inline jsonSerialize: x: 't -> string
        
        val inline (|StrStartWith|_|) : s: string -> x: string -> string option
        
        val inline implInterface<'t> : x: System.Type -> bool
        
        type IndexerBox<'tArg,'t> =
            
            new: [<InlineIfLambda>] func: ('tArg -> 't) -> IndexerBox<'tArg,'t>
            
            member Item: arg: 'tArg -> 't with get
        
        type 't arrList = ResizeArray<'t>
        type System.Collections.Generic.List<'t> with
            
            member inline GetReverseIndex: int * offset: int -> int
        type System.Collections.Generic.List<'t> with
            
            member
              inline GetSlice: startIndex: int option * endIndex: int option ->
                                 System.Collections.Generic.List<'t>
        
        module Itor =
            
            val inline moveNext:
              itor: System.Collections.Generic.IEnumerator<'t> -> bool
            
            val inline current:
              itor: System.Collections.Generic.IEnumerator<'t> -> 't
            
            val inline ofEnumerable:
              e: System.Collections.Generic.IEnumerable<'t> ->
                System.Collections.Generic.IEnumerator<'t>
        
        module ArrList =
            
            val inline map:
              mapping: ('t -> 'u) -> list: 't arrList -> 'u arrList
            
            val inline iter: action: ('t -> unit) -> list: 't arrList -> unit
            
            val inline fold:
              folder: ('state -> 't -> 'state) ->
                state: 'state -> list: 't arrList -> 'state
            
            val inline singleton: value: 't -> 't arrList
            
            val inline tryFind:
              predicate: ('t -> bool) -> list: 't arrList -> 't option
            
            val inline tryFindBack:
              predicate: ('t -> bool) -> list: 't arrList -> 't option
            
            val inline tryFindIndex:
              predicate: ('t -> bool) -> list: 't arrList -> int option
            
            val inline tryFindIndexBack:
              predicate: ('t -> bool) -> list: 't arrList -> int option
            
            val inline find: predicate: ('t -> bool) -> list: 't arrList -> 't
            
            val inline findBack:
              predicate: ('t -> bool) -> list: 't arrList -> 't
            
            val inline collect:
              mapping: ('t -> 'u) -> list: 't arrList -> 'u arrList
        
        module IList =
            
            val inline map:
              mapping: ('t -> 'u) ->
                list: System.Collections.Generic.IList<'t> -> 'u arrList
            
            val inline iter:
              action: ('t -> unit) ->
                list: System.Collections.Generic.IList<'t> -> unit
            
            val inline fold:
              folder: ('state -> 't -> 'state) ->
                state: 'state ->
                list: System.Collections.Generic.IList<'t> -> 'state
            
            val inline tryFind:
              predicate: ('t -> bool) ->
                list: System.Collections.Generic.IList<'t> -> 't option
            
            val inline tryFindBack:
              predicate: ('t -> bool) ->
                list: System.Collections.Generic.IList<'t> -> 't option
            
            val inline tryFindIndex:
              predicate: ('t -> bool) ->
                list: System.Collections.Generic.IList<'t> -> int option
            
            val inline tryFindIndexBack:
              predicate: ('t -> bool) ->
                list: System.Collections.Generic.IList<'t> -> int option
            
            val inline find:
              predicate: ('t -> bool) ->
                list: System.Collections.Generic.IList<'t> -> 't
            
            val inline findBack:
              predicate: ('t -> bool) ->
                list: System.Collections.Generic.IList<'t> -> 't
        
        module Dict =
            
            val inline deconstructPair:
              p: System.Collections.Generic.KeyValuePair<'k,'v> -> 'k * 'v
            
            val inline iter:
              action: ('k -> 'v -> unit) ->
                dict: System.Collections.Generic.IReadOnlyDictionary<'k,'v> ->
                unit
            
            val inline addRange:
              this: System.Collections.Generic.Dictionary<'k,'v> ->
                keys: 'k array ->
                dict: System.Collections.Generic.IReadOnlyDictionary<'k,'v> ->
                unit when 'k: equality
            
            val inline addAll:
              this: System.Collections.Generic.Dictionary<'k,'v> ->
                dict: System.Collections.Generic.IReadOnlyDictionary<'k,'v> ->
                unit
            
            val inline tryFind:
              predicate: ('k -> 'v -> bool) ->
                dict: System.Collections.Generic.IReadOnlyDictionary<'k,'v> ->
                ('k * 'v) option
            
            val inline find:
              predicate: ('k -> 'v -> bool) ->
                dict: System.Collections.Generic.IReadOnlyDictionary<'k,'v> ->
                'k * 'v
            
            val inline tryGetValue:
              key: 'k ->
                dict: System.Collections.Generic.IReadOnlyDictionary<'k,'v> ->
                'v option
        type System.Collections.Generic.Dictionary<'k,'v> with
            
            member
              inline Append: d: System.Collections.Generic.IReadOnlyDictionary<'k,
                                                                               'v> ->
                               unit
        
        type PipeLineChain<'t> =
            
            new: f: ('t -> 't) -> PipeLineChain<'t>
            
            new: fs: ('t -> 't) seq -> PipeLineChain<'t>
            
            static member
              FromFunc: fs: System.Func<'t,'t> seq -> PipeLineChain<'t>
            
            static member FromFunc: f: System.Func<'t,'t> -> PipeLineChain<'t>
            
            member Bind: g: ('t -> 't) -> unit
            
            member BindFunc: func: System.Func<'t,'t> -> unit
            
            member Invoke: x: 't -> 't
            
            member Unbind: g: ('t -> 't) -> bool
            
            member Inner: ('t -> 't) arrList with get
        
        module PipeLineChain =
            
            val inline ofSeq: fs: ('t -> 't) seq -> PipeLineChain<'t>
            
            val inline ofList: fs: ('t -> 't) list -> PipeLineChain<'t>
            
            val inline ofArray: fs: ('t -> 't) array -> PipeLineChain<'t>
            
            val inline singleton: f: ('t -> 't) -> PipeLineChain<'t>
            
            val inline bind: chain: PipeLineChain<'t> -> f: ('t -> 't) -> unit
        
        val inline dict':
          a: ('a * 'b) seq -> System.Collections.Generic.Dictionary<'a,'b>
            when 'a: equality
        
        val inline readOnlyDict':
          a: ('a * 'b) seq ->
            System.Collections.Generic.IReadOnlyDictionary<'a,'b>
            when 'a: equality
        
        val colorAt:
          arr: byte array ->
            w: int -> x: int -> y: int -> struct (byte * byte * byte * byte)
        
        val randomString: length: int -> string
        
        val bytesFromUrl: url: string -> byte array
        
        val bytesFromUrlAsync: url: string -> Async<Result<byte array,string>>
        
        val bash: cmd: string -> string
        
        val cmd: cmd: string -> string
        
        val pwsh: cmd: string -> string
        
        [<Measure>]
        type sec
        
        val inline getEnv: s: string -> System.String
        
        val inline tryGetEnv: s: string -> System.String option

