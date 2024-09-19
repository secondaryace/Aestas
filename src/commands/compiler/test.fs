module Aestas.Commands.Compiler.Test
open FSharp.Text.Lexing
let parse code = 
    // let lexbuf = LexBuffer<char>.FromString code
    // let rec go() =
    //     if lexbuf.IsPastEndOfStream then ()
    //     else 
    //         printf "%A, " (Lexer.read lexbuf)
    //         go()
    // go()
    let lexbuf = LexBuffer<char>.FromString code
    try
        let res = Parser.parse Lexer.read lexbuf
        res
    with e -> failwithf "parse error: %A\nat: (%d, %d), %A, %A" e lexbuf.EndPos.Line lexbuf.EndPos.Column lexbuf.Lexeme (Lexer.read lexbuf)
let test (code: string) = 
    let ast = code.Trim() |> parse
    printfn "ast ("; ast |> List.iter (printfn "%A") ; printfn ")\nrun:"
    let ctx, ret = Runtime.run (Runtime.makeContext Runtime.LanguagePrimitives.operators Map.empty Map.empty (printf "%s") []) ast
    printfn "\n%A" ctx
    printfn "returns: %A" ret
[<EntryPoint>]
let main args =
    match args with
    | [|"test"|] ->
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
            """
(raise a)
            """
            """
1+2+3+4+5
            """
            """
let 我使用unicode = "确实"
            """
            """
let a, b, c = 1, 2, 3
let h, t... = 1, 2, 3
let v = 1, 2, 3
v=(_), v=(_, _), v=(_, ...), v=(_, _, _, _, ...)
            """
            """
let sum tuple = (
    let go tuple acc = (
        if tuple=() then acc else (
            let h, t... = tuple
            go t (acc+h)
        )
    )
    go tuple ()
)
sum (1, 2, 3, 4, 5), sum ("Mai ", "Nemu ", "Isu ", "Anon ", "Chihaya ")
            """
            """
print (type (1), type (1,), type ())
            """
            """
print (type (^()), type (^ ()), type (^ 1 2 3), type (^ 1^2 3), type (^ 1 ^ 2 3))
            """
            """
let rev tuple = (
    let go tuple acc = (
        if tuple=() then acc else (
            let h, t... = tuple
            go t (cons h acc)
        )
    )
    go tuple ()
)
rev (seq 0 10), concat (10,) (seq 5 0)
            """
            """
let qsort tuple = (
    if tuple=() then () else (
        let h, t... = tuple
        let l, r = partition (curry > h) t
        print l r
        concat (qsort l) (h,) (qsort r)
    )
)
qsort (^ 10 9 1 29 -5 6 30 -20 6 3 12 16)
            """
            """
let o = object (aaa, 5) (asasa, "ooo") (getName, (lambda () -> "colin"))
let a = o.aaa
let b = o."aaa"
let c = o.("asa"+"sa")
let d = o.getName()
ls binds
            """
            """
let fact f n = if n=0 then 1 else (* n (f f (- n 1)) )
let notY f x = f f x  
notY fact 5
            """
        ]
        testCode |> List.iteri (fun i src -> printfn "\n%d:" i; try test src with ex -> printfn "error: %A" ex.Message)
    | [|"run"; code|] -> test code
    | _ -> printfn "Usage: test | run <code>"
    // 1 1 2 3 5 8 13 21
    0