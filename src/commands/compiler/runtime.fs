module rec Aestas.Commands.Compiler.Runtime
open System
open Aestas.Commands.Compiler.Language
[<StructuredFormatDisplay("{Print}")>]
type Value = 
    | String of string
    | Bool of bool
    | Number of float
    | Function of string list * Ast list
    | Tuple of Value list
    | Object of Map<string, Value>
    | Wildcard
    | TupleTail
    | ExternFunction of (Context -> Value list -> (Context * Value))
    | ExternValue of obj
    member this.Print with get() = Value.Display this
    static member Display value =
        match value with
        | String s -> sprintf "\"%s\"" s
        | Wildcard -> "_"
        | TupleTail -> "..."
        | Bool b -> string b
        | Number f -> string f
        | Function (params', asts) ->
            sprintf "λ %s -> (%s)" 
                ((match params' with [] -> "()" | x -> x |> List.map string |> String.concat " ") ) 
                (asts |> List.map string |> String.concat "\n")
        | ExternFunction f -> sprintf "externf %A" f
        | ExternValue o -> sprintf "extern %A: %s" o (o.GetType().Name)
        | Tuple(vs) ->
            vs |> List.map string |> String.concat ", "
            |> sprintf "(%s)"
        | Object kv ->
            kv |> Map.map (fun k v -> sprintf "(%s, %A)" k v) 
            |> Map.values |> String.concat " " |> sprintf "(object %s)"
[<StructuredFormatDisplay("{Print}")>]
type Context = {
    shared: Map<string, Value>
    binds: Map<string, Value>
    args: Map<string, Value>
    print: string -> unit
    trace: string list
} 
    with
        member this.Print with get() = Context.Display this true true true
        static member Display context shared binds args =
            let mapString title map = 
                title + Map.fold (fun s id v -> s + sprintf "\n    %s = %A" id v) "" map in
            let ls = [shared; binds; args] in
            ls |> List.mapi (fun i x ->
                match i, x with
                | 0, true -> mapString "shared:" context.shared |> Some
                | 1, true -> mapString "binds:" context.binds |> Some
                | 2, true -> mapString "args:" context.args |> Some
                | _ -> None
            ) |> List.choose id |> String.concat "\n"
            // sprintf "shared: %s\nbinds: %s\nargs: %s" 
            //     (mapString context.shared) 
            //     (mapString context.binds) 
            //     (mapString context.args) 
exception AestasScriptException of string*string list
let inline failwithtf t fmt = Printf.kprintf (fun s -> AestasScriptException(s, t) |> raise) fmt
let inline failwitht t s = AestasScriptException(s, t) |> raise 
module LanguagePrimitives =
    let rec eq ctx args =
        ctx,
        match args with
        | [Wildcard; Tuple []] -> Bool false
        | [Tuple []; Wildcard] -> Bool false
        | [Wildcard; _] -> Bool true
        | [_; Wildcard] -> Bool true
        | [Bool a; Bool b] -> Bool (a=b)
        | [Number a; Number b] -> Bool (a=b)
        | [String a; String b] -> Bool (a=b)
        | [Function (a, c); Function (b, d)] -> Bool (a=b && c.Equals d)
        | [Tuple a; Tuple b] -> 
            let rec go a b =
                match a, b with
                | [], [] -> true
                | [TupleTail], _ -> true
                | _, [TupleTail] -> true
                | h::t, h'::t' -> match eq ctx [h; h'] |> snd with Bool true -> go t t' | _ -> false
                | _, _ -> false in
            Bool (go a b)
        | [_; _] -> Bool false
        | _ -> failwithtf ctx.trace "Couldn't apply %A to '='" args
    let ne ctx args =
        match eq ctx args with
        | ctx, Bool x -> ctx, Bool (not x)
        | _ -> failwitht ctx.trace "Unreachable" 
    let rec calc s ctx args =
        ctx, 
        match s, args with
        | "and", [Bool a; Bool b] -> Bool (a&&b)
        | "or", [Bool a; Bool b] -> Bool (a||b)
        | "+", [Number a; Number b] -> Number (a+b)
        | "-", [Number a; Number b] -> Number (a-b)
        | "+", [Tuple []; x] -> x
        | "+", [x; Tuple []] -> x
        | "+", [Number a] -> Number (a)
        | "-", [Number a] -> Number (-a)
        | "*", [Number a; Number b] -> Number (a*b)
        | "/", [Number a; Number b] -> Number (a/b)
        | "+", [String a; String b] -> String (a+b)
        | _, _ -> failwithtf ctx.trace "Couldn't apply %A to %s" args s
    let map ctx args =
        match args with
        | [f; Tuple vs] -> 
            let rec go ctx acc = function
            | [] -> ctx, Tuple (List.rev acc)
            | h::t -> 
                let ctx, v = exec ctx f [h] in
                go ctx (v::acc) t in
            go ctx [] vs
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let gt ctx args =
        ctx,
        match args with
        | [Number x; Number y] -> Bool (x>y)
        | _ -> failwithtf ctx.trace "Couldn't apply %A to '>'" args
    let lt ctx args =
        ctx,
        match args with
        | [Number x; Number y] -> Bool (x<y)
        | _ -> failwithtf ctx.trace "Couldn't apply %A to '<'" args
    let gteq ctx args =
        ctx,
        match args with
        | [Number x; Number y] -> Bool (x>=y)
        | _ -> failwithtf ctx.trace "Couldn't apply %A to '>='" args
    let lteq ctx args =
        ctx,
        match args with
        | [Number x; Number y] -> Bool (x<=y)
        | _ -> failwithtf ctx.trace "Couldn't apply %A to '<='" args
    let cons ctx args =
        let rec go = function
        | [x; Tuple ts] -> x::ts
        | h::t ->
            h::go t
        | _ -> failwitht ctx.trace "Invalid Arguments" in
        ctx, Tuple (go args)
    let id ctx args = match args with [x] -> ctx, x | _ -> failwitht ctx.trace "Invalid"
    let type' ctx args =
        ctx, 
        String (
            let rec getType = function
            | String _ -> "string"
            | Wildcard -> "_"
            | Bool _ -> "bool"
            | Number _ -> "number"
            | Function _ -> "function"
            | Object _ -> "object"
            | Tuple [] -> "unit"
            | Tuple ts -> sprintf "tuple<%s>" (ts |> List.map getType |> String.concat ", ")
            | TupleTail -> "tuple"
            | ExternFunction _ -> "externf"
            | ExternValue o -> sprintf "extern<%s>" (o.GetType().Name) in
            match args with
            | [x] -> getType x
            | _ -> failwithf "Invalid type %A" args
        )
    let tuple ctx args =
        match args with
        | [] -> ctx, Tuple []
        | _ -> ctx, Tuple args
    let seq' ctx args =
        let rec generator curr target delta acc = 
            if curr >= target && delta > 0. then acc
            elif curr <= target && delta < 0. then acc
            else generator (curr+delta) target delta (Number curr::acc) in
        match args with
        | [Number a; Number b] -> 
            if a = b then ctx, Tuple [Number a]
            elif Math.Truncate a = a && Math.Truncate b = b then
                // generator returns reverse sequence, sometimes we can make use of it
                if a < b then ctx, generator (b-1.) (a-1.) -1. [] |> Tuple
                else ctx, generator b a 1. [] |> Tuple
            else
                ctx, generator a b (if a<b then 1. else -1.) [] |> List.rev |> Tuple
        | [Number a; Number b; Number d] when (a<b && d>0) || (a>b && d<0) ->
            ctx, generator a b d [] |> List.rev |> Tuple
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let rev ctx args =
        match args with
        | [Tuple ls] -> ctx, ls |> List.rev |> Tuple
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let concat ctx args =
        let rec go acc = function
        | [] -> acc
        | Tuple ts::t -> go (ts::acc) t
        | _ -> failwitht ctx.trace "Invalid Arguments" in
        ctx, go [] args |> List.rev |> List.concat |> Tuple
    let filter ctx args =
        match args with
        | [pred; Tuple ls] -> 
            ctx, ls |> List.filter (fun x ->
                match exec ctx pred [x] with
                | _, Bool b -> b
                | _ -> failwitht ctx.trace "Predicate should return a bool value"
            ) |> Tuple
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let partition ctx args =
        match args with
        | [pred; Tuple ls] -> 
            let a, b =
                ls |> List.partition (fun x ->
                    match exec ctx pred [x] with
                    | _, Bool b -> b
                    | _ -> failwitht ctx.trace "Predicate should return a bool value"
                ) in
            ctx, Tuple [Tuple a; Tuple b]
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let object ctx args =
        ctx, args |> List.fold (fun map -> 
            function
            | Tuple[String s; v] -> Map.add s v map
            | _ -> failwitht ctx.trace "Invalid Arguments"
        ) Map.empty |> Object
    let makeObject ctx args =
        ctx, 
        match args with
        | [Tuple ts] ->
            ts |> List.fold (fun map -> 
                function
                | Tuple[String s; v] -> Map.add s v map
                | _ -> failwitht ctx.trace "Invalid Arguments"
            ) Map.empty |> Object
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let curry ctx args =
        match args with
        | func::ls -> 
            ctx,
            ExternFunction (fun ctx args -> 
                exec ctx func (ls @ args)
            )
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let getField ctx args =
        let getField o s = 
            match Map.tryFind s o with
            | Some v -> v
            | None -> Tuple [] in
        match args with
        | [Object o; String s] -> ctx,  getField o s
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let ls ctx args =
        match args with
        | [] -> Context.Display ctx true true true |> ctx.print; ctx, Tuple []
        | _ ->        
            let set = 
                args 
                |> List.choose (function String s -> Some s | x -> None) 
                |> Set.ofList in
            let shared = Set.contains "shared" set in
            let binds = Set.contains "binds" set in
            let args = Set.contains "args" set in
            Context.Display ctx shared binds args |> ctx.print; ctx, Tuple []
    let operators = Map.ofList [
        "+", calc "+" |> ExternFunction
        "-", calc "-" |> ExternFunction
        "*", calc "*" |> ExternFunction
        "/", calc "/" |> ExternFunction
        "=", ExternFunction eq
        "<>", ExternFunction ne
        ">", ExternFunction gt
        "<", ExternFunction lt
        ">=", ExternFunction gteq
        "<=", ExternFunction lteq
        ".", ExternFunction getField
        "and", calc "and" |> ExternFunction
        "or", calc "or" |> ExternFunction
        "id", ExternFunction id
        "^", ExternFunction tuple
        "object", ExternFunction object
        "mkobj", ExternFunction makeObject
        "type", ExternFunction type'
        "curry", ExternFunction curry
        "map", ExternFunction map
        "cons", ExternFunction cons
        "seq", ExternFunction seq'
        "rev", ExternFunction rev
        "filter", ExternFunction filter
        "partition", ExternFunction partition
        "concat", ExternFunction concat
        "print", ExternFunction (fun ctx args -> 
            sprintf "%s\n" (List.map (function String s -> s | x -> string x) args |> String.concat " ") 
            |> ctx.print; ctx, Tuple []
        )
        "write", ExternFunction (fun ctx args -> 
            sprintf "%s" (List.map (function String s -> s | x -> string x) args |> String.concat " ") 
            |> ctx.print; ctx, Tuple []
        )
        "ls", ExternFunction ls
    ]
let makeContext shared binds args print trace = {
    shared = shared
    binds = binds
    args = args
    print = print
    trace = trace
    }
let run (ctx: Context) (asts: Ast list) =
    match asts with
    | [Call (Identifier x, [])] -> 
        match Map.tryFind x ctx.binds with
        | None -> failwitht ctx.trace "Function not found"
        | Some x ->
            exec ctx x []
    | _ ->
        let rec go ctx = function
        | [] -> ctx, Tuple []
        | [x] -> tuple ctx x
        | h::t -> 
            let ctx, _ = tuple ctx h
            go ctx t in
        go ctx asts
let bindValue (ctx: Context) (id: string) v =
    if id.StartsWith '-' then failwitht ctx.trace "Invalid identifier" 
    elif id = "_" then ctx.binds
    else
        let id = if id.StartsWith "@" then id[1..] else id in
        if Map.containsKey id ctx.binds then Map.change id (fun _ -> Some v) ctx.binds else Map.add id v ctx.binds
let rec tuple (ctx: Context) (ast: Ast) =
    match ast with
    | Ast.Tuple exprs ->
        let rec go ctx acc = function
        | [] -> ctx, List.rev acc
        | h::t -> 
            let ctx, v = tuple ctx h
            go ctx (v::acc) t in
        let ctx, vs = go ctx [] exprs in
        ctx, Tuple vs
    | _ -> expr ctx ast
let rec expr (ctx: Context) (ast: Ast) =
    match ast with
    | Let (pattern, lines) -> 
        let rec matching ctx = function
        | Identifier id, value -> {ctx with binds = bindValue ctx id value}
        | Ast.Tuple tree, Tuple vtree ->
            let rec go ctx = function
            | [], [] -> ctx
            | [TailPattern id], ls -> matching ctx (Identifier id, Tuple ls)
            | h::t, h'::t' -> go (matching ctx (h, h')) (t, t')
            | l, l' -> failwithtf ctx.trace "Matching failed: (%A) -x> (%A)" l l'
            in
            go ctx (tree, vtree)
        | pattern, value -> failwithtf ctx.trace "Matching failed: (%A) -x> (%A)" pattern value
        in
        let ctx, value = run ctx lines in
        matching ctx (pattern, value), Tuple []
    | Lambda (plist, lines) -> ctx, Function(plist, lines)
    | Call (fexpr, args) -> 
        let ctx, f = tuple ctx fexpr in
        let rec go ctx acc = function
        | [] -> ctx, List.rev acc
        | h::t -> 
            let ctx, v = tuple ctx h
            go ctx (v::acc) t in
        let ctx, args = go ctx [] args in
        exec ctx f args
        //exec { ctx with trace = sprintf "exec %A" fexpr ::ctx.trace} f args
    | If (cond, brTrue, brFalse) ->
        let ctx, vcond = run ctx cond in
        match vcond, brFalse with
        | Bool true, _ ->
            let ctx, vbrTrue = run ctx brTrue in
            ctx, vbrTrue
        | Bool false, _ ->
            let ctx, vbrFalse = run ctx brFalse in
            ctx, vbrFalse
        | _ -> failwitht ctx.trace "Condition should be bool"
    | Ast.String s -> ctx, String s
    | Ast.Bool b -> ctx, Bool b
    | Ast.Number f -> ctx, Number f
    | Unit -> ctx, Tuple []
    | Identifier id -> 
        if id = "_" then ctx, Wildcard
        elif id.StartsWith "--" then ctx, String id[2..]
        elif id.StartsWith '-' && id <> "-" then ctx, String id[1..]
        elif id.StartsWith '@' then
            match Map.tryFind id[1..] ctx.args with
            | None -> { ctx with trace = sprintf "Name %s is not defined" id::ctx.trace }, Tuple []
            | Some x -> ctx, x
        elif id = "..." then ctx, TupleTail
        else
            match Map.tryFind id ctx.args with
            | Some x -> ctx, x 
            | None -> 
                match Map.tryFind id ctx.binds with
                | Some x -> ctx, x
                | None -> 
                    match Map.tryFind id ctx.shared with
                    | Some x -> ctx, x
                    | None -> { ctx with trace = sprintf "Name %s is not defined" id::ctx.trace }, String id
            // let inline imap (f) = function None -> f() | Some x -> Some x 
            // Map.tryFind id ctx.args
            // |> imap (fun () -> Map.tryFind id ctx.binds)
            // |> imap (fun () -> Map.tryFind id ctx.shared)
            // |> Option.map (fun x -> ctx, x)
            // |> Option.defaultWith (fun () -> { ctx with trace = sprintf "Name %s is not defined" id::ctx.trace }, Flag id)
    | _ -> failwithtf ctx.trace "Unreachable %A" ast
let exec (ctx: Context) (callee: Value) (args: Value list) =
    //printfn "exec %A %A" callee args
    match callee with
    | Function (params', asts) -> 
        let rec go params' args acc unameCount =
            match params', args with
            | _, [] -> Map.ofList acc
            | [], h::t ->
                go [] t ((string unameCount, h)::acc) (unameCount+1)
            | h::t, h'::t' -> 
                go t t' ((h, h')::acc) unameCount in
        let ctx', value = 
            run { 
                ctx with 
                    args = (go params' args [] 0) |> Map.fold (fun a k v ->
                        a |> Map.change k (fun _ -> Some v)
                    ) ctx.args
            } asts in
        { ctx with shared = ctx'.shared } , value
    | ExternFunction f ->
        let ctx', value = f ctx args in
        { ctx with shared = ctx'.shared }, value
    | _ -> failwithtf ctx.trace "Error when calling %A: Should call a function" callee