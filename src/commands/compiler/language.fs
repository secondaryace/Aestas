module Aestas.Commands.Compiler.Language 
[<StructuredFormatDisplay("{Print}")>]
type Ast =
    | Let of Ast * (Ast list)
    | Lambda of string list * (Ast list)
    | If of (Ast list) * (Ast list) * (Ast list)
    | Call of Ast * Ast list
    | Tuple of Ast list
    | String of string
    | Bool of bool
    | Number of float
    | Identifier of string
    | TailPattern of string
    | Unit
    member this.Print with get() = Ast.Display this
    static member Display ast =
        match ast with
        | Let(id, lines) -> 
            lines |> List.map string |> String.concat "\n"
            |> sprintf "let %A = (%s)" id
        | Lambda(plist, lines) -> 
            sprintf "lambda %s -> (%s)" 
                (match plist with [] -> "()" | x -> x |> List.map string |> String.concat " ") 
                (lines |> List.map string |> String.concat "\n")
        | If(condLines, brTrueLines, brFalseLines) ->
            sprintf "if (%s) then (%s) else (%s)" 
                (condLines |> List.map string |> String.concat "\n") 
                (brTrueLines |> List.map string |> String.concat "\n")
                (brFalseLines |> List.map string |> String.concat "\n") 
        | Call(f, args) ->
            sprintf "(%A) %s" f (args |> List.map (string >> sprintf "(%s)") |> String.concat " ")
        | Tuple(vs) ->
            vs |> List.map string |> String.concat ", "
            |> sprintf "(%s)"
        | String s -> sprintf "\"%s\"" s
        | Bool b -> string b
        | Number f -> string f
        | Identifier s -> s
        | TailPattern s -> sprintf "%s..." s
        | Unit -> "()"
// module JsonParsing 
//     type JsonValue = 
//         | Assoc of (string * JsonValue) list
//         | Bool of bool
//         | Float of float
//         | Int of int
//         | List of JsonValue list
//         | Null
//         | String of string