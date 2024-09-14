module rec Aestas.Commands.Compiler.Runtime
open Aestas.Commands.Compiler.Language
[<StructuredFormatDisplay("{Print}")>]
type Value = 
    | String of string
    | Flag of string
    | Bool of bool
    | Number of float
    | Function of string list * Ast list
    | Tuple of Value list
    | Unit
    | Wildcard
    | TupleTail
    | FSharpFunction of (Context -> Value list -> (Context * Value))
    member this.Print with get() = Value.Display this
    static member Display value =
        match value with
        | String s -> sprintf "\"%s\"" s
        | Flag s -> if s.Length = 1 then sprintf "-%s" s else sprintf "--%s" s
        | Wildcard -> "_"
        | TupleTail -> "..."
        | Bool b -> string b
        | Number f -> string f
        | Function (params', asts) ->
            sprintf "lambda %s -> (%s)" (params' |> String.concat ", ") (asts |> List.map string |> String.concat "\n")
        | FSharpFunction f -> sprintf "FSharpFunction %A" f
        | Tuple(vs) ->
            vs |> List.map string |> String.concat ", "
            |> sprintf "(%s)"
        | Unit -> "()"
[<StructuredFormatDisplay("{Print}")>]
type Context = {
    binds: Map<string, Value>
    args: Map<string, Value>
    print: string -> unit
    trace: string list
} 
    with
        member this.Print with get() = Context.Display this
        static member Display context =
            let mapString map = Map.fold (fun s id v -> s+sprintf "\n    %s = %A" id v) "" map
            sprintf "binds: %s\nargs: %s" (mapString context.binds) (mapString context.args) 
exception AestasScriptException of string*string list
let inline failwithtf t fmt = Printf.kprintf (fun s -> AestasScriptException(s, t) |> raise) fmt
let inline failwitht t s = AestasScriptException(s, t) |> raise 
module Prim =
    let eq ctx args =
        ctx,
        match args with
        | [Unit; Unit] -> Bool true
        | [Wildcard; Unit] -> Bool false
        | [Unit; Wildcard] -> Bool false
        | [Wildcard; _] -> Bool true
        | [_; Wildcard] -> Bool true
        | [Bool a; Bool b] -> Bool (a=b)
        | [Number a; Number b] -> Bool (a=b)
        | [String a; String b] -> Bool (a=b)
        | [String a; Flag b] -> Bool (a=b)
        | [Flag a; String b] -> Bool (a=b)
        | [Function (a, c); Function (b, d)] -> Bool (a=b && c.Equals d)
        | [Tuple a; Tuple b] -> 
            let rec go a b =
                match a, b with
                | [], [] -> true
                | [TupleTail], _ -> true
                | _, [TupleTail] -> true
                | h::t, h'::t' -> match eq ctx [h; h'] |> snd with Bool true -> go t t' | _ -> false
                | _, _ -> false
            Bool (go a b)
        | [_; _] -> Bool false
        | _ -> failwithtf ctx.trace "Couldn't apply %A to '='" args
    let rec calc s ctx args =
        ctx, 
        match s, args with
        | "and", [Bool a; Bool b] -> Bool (a&&b)
        | "or", [Bool a; Bool b] -> Bool (a||b)
        | "+", [Number a; Number b] -> Number (a+b)
        | "-", [Number a; Number b] -> Number (a-b)
        | "+", [Unit; x] -> x
        | "+", [x; Unit] -> x
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
                let ctx, v = exec ctx f [h]
                go ctx (v::acc) t
            go ctx [] vs
        | _ -> failwitht ctx.trace "Invalid Arguments"
    let id ctx args = match args with [x] -> ctx, x | _ -> failwitht ctx.trace "Invalid"
    let type' ctx args =
        ctx, 
        String (
            let rec getType = function
            | String _ -> "string"
            | Flag _ -> "flag"
            | Wildcard -> "_"
            | Bool _ -> "bool"
            | Number _ -> "number"
            | Function _ -> "function"
            | Tuple ts -> sprintf "tuple<%s>" (ts |> List.map getType |> String.concat ", ")
            | TupleTail -> "tuple"
            | Unit -> "unit"
            | FSharpFunction _ -> "fsharpfunction"
            match args with
            | [x] -> getType x
            | _ -> failwithf "Invalid type %A" args
        )

    let operators = Map.ofList [
        "+", calc "+" |> FSharpFunction
        "-", calc "-" |> FSharpFunction
        "*", calc "*" |> FSharpFunction
        "/", calc "/" |> FSharpFunction
        "=", FSharpFunction eq
        "and", calc "and" |> FSharpFunction
        "or", calc "or" |> FSharpFunction
        "id", FSharpFunction id
        "^", FSharpFunction (fun ctx args -> ctx, Tuple args)
        "type", FSharpFunction type'
        "map", FSharpFunction map
        "print", FSharpFunction (fun ctx args -> 
            sprintf "%s\n" (List.map (function String s -> s | x -> string x) args |> String.concat " ") 
            |> ctx.print
            ctx, Unit
        )
        "write", FSharpFunction (fun ctx args -> 
            sprintf "%s" (List.map (function String s -> s | x -> string x) args |> String.concat " ") 
            |> ctx.print
            ctx, Unit
        )
        "ls", FSharpFunction (fun ctx args -> 
            sprintf "%A\n" ctx |> ctx.print
            ctx, Unit
        )
    ]
let makeContext binds args print trace = {
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
        | [] -> ctx, Unit
        | [x] -> tuple ctx x
        | h::t -> 
            let ctx, _ = tuple ctx h
            go ctx t
        go ctx asts
let bindValue (ctx: Context) (id: string) v =
    if id.StartsWith '-' then failwitht ctx.trace "Invalid identifier" 
    elif id = "_" then ctx.binds
    else
        let id = if id.StartsWith "@" then id[1..] else id
        if Map.containsKey id ctx.binds then Map.change id (fun _ -> Some v) ctx.binds else Map.add id v ctx.binds
let rec tuple (ctx: Context) (ast: Ast) =
    match ast with
    | Ast.Tuple exprs ->
        let rec go ctx acc = function
        | [] -> ctx, List.rev acc
        | h::t -> 
            let ctx, v = tuple ctx h
            go ctx (v::acc) t
        let ctx, vs = go ctx [] exprs
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
            go ctx (tree, vtree)
        | pattern, value -> failwithtf ctx.trace "Matching failed: (%A) -x> (%A)" pattern value
        let ctx, value = run ctx lines
        matching ctx (pattern, value), Unit
    | Lambda (plist, lines) -> ctx, Function(plist, lines)
    | Call (fexpr, args) -> 
        let ctx, f = tuple ctx fexpr
        let rec go ctx acc = function
        | [] -> ctx, List.rev acc
        | h::t -> 
            let ctx, v = tuple ctx h
            go ctx (v::acc) t
        let ctx, args = go ctx [] args
        exec ctx f args
    | If (cond, brTrue, brFalse) ->
        let ctx, vcond = run ctx cond
        match vcond, brFalse with
        | Bool true, _ ->
            let ctx, vbrTrue = run ctx brTrue
            ctx, vbrTrue
        | Bool false, _ ->
            let ctx, vbrFalse = run ctx brFalse
            ctx, vbrFalse
        | _ -> failwitht ctx.trace "Condition should be bool"
    | Ast.String s -> ctx, String s
    | Ast.Bool b -> ctx, Bool b
    | Ast.Number f -> ctx, Number f
    | Ast.Unit -> ctx, Unit
    | Identifier id -> 
        if id = "_" then ctx, Wildcard
        elif id.StartsWith "--" then ctx, Flag id[2..]
        elif id.StartsWith '-' && id <> "-" then ctx, Flag id[1..]
        elif id.StartsWith '@' then
            match Map.tryFind id[1..] ctx.args with
            | None -> { ctx with trace = sprintf "Name %s is not defined" id::ctx.trace }, Unit
            | Some x -> ctx, x
        elif id = "..." then ctx, TupleTail
        else
            match Map.tryFind id ctx.args with
            | None -> 
                match Map.tryFind id ctx.binds with
                | None -> { ctx with trace = sprintf "Name %s is not defined" id::ctx.trace }, Flag id
                | Some x -> ctx, x
            | Some x -> ctx, x
    | _ -> failwithtf ctx.trace "Unreachable %A" ast
let exec (ctx: Context) (callee: Value) (args: Value list) =
    //printfn "exec %A %A" callee args
    let args' = ctx.args
    match callee with
    | Function (params', asts) -> 
        let rec go params' args acc unameCount =
            match params', args with
            | _, [] -> Map.ofList acc
            | [], h::t ->
                go [] t ((string unameCount, h)::acc) (unameCount+1)
            | h::t, h'::t' -> 
                go t t' ((h, h')::acc) unameCount
        let ctx, value =  run { ctx with args = go params' args [] 0 } asts
        { ctx with args = args' }, value
    | FSharpFunction f ->
        let ctx, value = f ctx args 
        { ctx with args = args' }, value
    | _ -> failwithtf ctx.trace "Error when calling %A: Should call a function" callee