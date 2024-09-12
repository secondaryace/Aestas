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
    | FSharpFunction of (Context -> Value list -> (Context * Value))
    member this.Print with get() = Value.Display this
    static member Display value =
        match value with
        | String s -> sprintf "\"%s\"" s
        | Flag s -> if s.Length = 1 then sprintf "-%s" s else sprintf "--%s" s
        | Wildcard -> "_"
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
} 
    with
        member this.Print with get() = Context.Display this
        static member Display context =
            let mapString map = Map.fold (fun s id v -> s+sprintf "\n    %s = %A" id v) "" map
            sprintf "binds: %s\nargs: %s" (mapString context.binds) (mapString context.args) 
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
                | h::t, h'::t' -> match Prim.eq ctx [h; h'] |> snd with Bool true -> go t t' | _ -> false
                | _, _ -> false
            Bool (go a b)
        | [_; _] -> Bool false
        | _ -> failwithf "Couldn't apply %A to '='" args
    let rec calc s ctx args =
        ctx, 
        match s, args with
        | "and", [Bool a; Bool b] -> Bool (a&&b)
        | "or", [Bool a; Bool b] -> Bool (a||b)
        | "+", [Number a; Number b] -> Number (a+b)
        | "-", [Number a; Number b] -> Number (a-b)
        | "+", [Number a] -> Number (a)
        | "-", [Number a] -> Number (-a)
        | "*", [Number a; Number b] -> Number (a*b)
        | "/", [Number a; Number b] -> Number (a/b)
        | "+", [String a; String b] -> String (a+b)
        | _, _ -> failwithf "Couldn't apply %A to %s" args s
    let map ctx args =
        match args with
        | [f; Tuple vs] -> 
            let rec go ctx acc = function
            | [] -> ctx, Tuple (List.rev acc)
            | h::t -> 
                let ctx, v = exec ctx f [h]
                go ctx (v::acc) t
            go ctx [] vs
        | _ -> failwith "Invalid Arguments"
    
    let operators = Map.ofList [
        "+", calc "+" |> FSharpFunction
        "-", calc "-" |> FSharpFunction
        "*", calc "*" |> FSharpFunction
        "/", calc "/" |> FSharpFunction
        "=", FSharpFunction eq
        "and", calc "and" |> FSharpFunction
        "or", calc "or" |> FSharpFunction
        "id", FSharpFunction (fun ctx args -> match args with [x] -> ctx, x | _ -> failwith "Invalid")
        "^", FSharpFunction (fun ctx args -> ctx, Tuple args)
        "type", FSharpFunction (fun ctx args -> 
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
                | Unit -> "unit"
                | FSharpFunction _ -> "fsharpfunction"
                match args with
                | [x] -> getType x
                | _ -> failwithf "Invalid type %A" args
            )
        )
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
let makeContext binds args print = {binds = binds; args = args ; print = print}
let run (ctx: Context) (asts: Ast list) =
    match asts with
    | [Call (Identifier x, [])] -> 
        match Map.tryFind x ctx.binds with
        | None -> failwith ""
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
    if id.StartsWith '-' then failwith "Invalid identifier" 
    elif id = "_" then ctx.binds
    else
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
            | h::t, h'::t' -> go (matching ctx (h, h')) (t, t')
            | l, l' -> failwithf "Matching failed: (%A) -x> (%A)" l l'
            go ctx (tree, vtree)
        | pattern, value -> failwithf "Matching failed: (%A) -x> (%A)" pattern value
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
        | _ -> failwith "Condition should be bool"
    | Ast.String s -> ctx, String s
    | Ast.Bool b -> ctx, Bool b
    | Ast.Number f -> ctx, Number f
    | Identifier id -> 
        ctx, 
        if id = "_" then Wildcard
        elif id.StartsWith "--" then Flag id[2..]
        elif id.StartsWith '-' && id <> "-" then Flag id[1..]
        elif id.StartsWith '@' then
            match Map.tryFind id[1..] ctx.args with
            | None -> Unit
            | Some x -> x
        else
            match Map.tryFind id ctx.args with
            | None -> 
                match Map.tryFind id ctx.binds with
                | None -> Unit 
                | Some x -> x
            | Some x -> x
    | _ -> failwith "Unreachable"
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
    | _ -> failwithf "Error when calling %A: Should call a function" callee