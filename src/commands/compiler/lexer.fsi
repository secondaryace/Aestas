
module Aestas.Commands.Compiler.Lexer
open FSharp.Text.Lexing
open System
open System.Text
open Parser/// Rule read
val read: lexbuf: LexBuffer<char> -> token
/// Rule read_string
val read_string: str: string -> ignorequote: bool -> lexbuf: LexBuffer<char> -> token
