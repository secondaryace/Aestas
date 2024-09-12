namespace Aestas.Plugins
open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.Json
open Aestas.Core
open Aestas.AutoInit
open Aestas.Prim

module Sticker =
    type StickerInfo = {path: string; width: int; height: int}
    type Strickers = {stickers: Dictionary<string, StickerInfo>}
    let cache = Dictionary<string, byte[]>()
    type StickerParser =
        interface IAutoInit<string*ContentParser*(AestasBot -> StringBuilder -> unit), unit> with
            static member Init _ = 
                "sticker"
                , fun bot domain params' content ->
                    match bot.TryGetExtraData("stickers") with
                    | Some (:? Strickers as profile) when profile.stickers.ContainsKey content -> 
                        let sticker = profile.stickers[content]
                        if cache.ContainsKey sticker.path |> not then
                            cache.Add(sticker.path, File.ReadAllBytes sticker.path)
                        AestasImage(cache[sticker.path], $"image/{sticker.path.Split('.')[^0]}", sticker.width, sticker.height) |> Ok
                    | Some (:? Strickers as _) -> Error $"Couldn't find sticker {content}"
                    | _ -> Error "Couldn't find stickers data"
                , fun bot sb ->
                    sb.AppendLine("You may send stickers like #[sticker:name].") |> ignore
                    sb.AppendLine("e.g. #[sticker:happy].") |> ignore
                    match bot.TryGetExtraData("stickers") with
                    | Some (:? Strickers as stickers) -> 
                        sb.AppendLine("Available stickers: ") |> ignore
                        stickers.stickers |> Dict.iter (fun k v -> sb.Append(k).Append ' ' |> ignore)
                    | _ -> ()