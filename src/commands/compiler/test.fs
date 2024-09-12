module Aestas.Commands.Compiler.Test
open FSharp.Text.Lexing
let parse code = 
    let lexbuf = LexBuffer<char>.FromString code
    try
        let res = Parser.parse Lexer.read lexbuf
        res
    with e -> failwithf "parse error: %A\nat: (%d, %d), %A, %A" e.Message lexbuf.EndPos.Line lexbuf.EndPos.Column lexbuf.Lexeme (Lexer.read lexbuf)
let test (code: string) = 
    let ast = code.Trim() |> parse
    printfn "ast ("; ast |> List.iter (printfn "%A") ; printfn ")\nrun:"
    let ctx, ret = Runtime.run (Runtime.makeContext Runtime.Prim.operators Map.empty (printf "%s")) ast
    printfn "\n%A" ctx
    printfn "returns: %A" ret
[<EntryPoint>]
let main _ =
    let testCode = [
        """
let fib x = (
    let go x a b = (
        if x=1 then b else (go x-1 b a+b)
    )
    if (or x=1 x=2) then 1 else (go x-1 1 1)
)
    fib 5
        """
        """
let a, b, c = 1, "2", 3^3
print a b c
1, "a"      , 1+1       , * 5 4 , 1^4=_^4, 1^1^1^1^1^1=_^_^_^_^_^_
"""
        """
let isTuple2 x = if x=_^_ then true else false
let fst x = (
    if x=_^_ then (
        let a, b = x
        a
    ) else ()
)
isTuple2 1^2, isTuple2 ("a", fst), isTuple2 (1, 2, 3), isTuple2 1, fst "aaa"^"bbb"
        """
        """
map write (1, 2, 3, 4, 5)
print()
        """
        """
let f () = (
    print @0 @1 @2 @3 @4
)
f 1 2 3 4 5
f 1 2 3
f 1 2 3 -t --Test
        """
    ]
    testCode |> List.iteri (fun i src -> printfn "\n%d:" i; try test src with ex -> printfn "error: %A" ex.Message)
    // 1 1 2 3 5 8 13 21
    0