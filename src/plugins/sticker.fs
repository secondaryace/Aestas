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
    type StickerContent(name: string) =
        interface IAestasMappingContent with
            member _.Convert bot domain = 
                match bot.ExtraData("stickers") with
                | Some (:? Strickers as profile) -> 
                    let sticker = profile.stickers[name]
                    if cache.ContainsKey sticker.path |> not then
                        cache.Add(sticker.path, File.ReadAllBytes sticker.path)
                    AestasImage(cache[sticker.path], $"image/{sticker.path.Split('.')[^0]}", sticker.width, sticker.height)
                | _ -> AestasText("Error: No sticker profile found")
        interface IAutoInit<(ContentParam->IAestasMappingContent)*string*(AestasBot -> string), unit> with
            static member Init _ = 
                (fun (domain, params', content) ->
                    content |> StickerContent :> IAestasMappingContent), "sticker", (fun bot ->
                        let sb = StringBuilder()
                        sb.AppendLine("You may send stickers like #[sticker:name].") |> ignore
                        sb.AppendLine("e.g. #[sticker:happy].") |> ignore
                        match bot.ExtraData("stickers") with
                        | Some (:? Strickers as stickers) -> 
                            sb.AppendLine("Available stickers: ") |> ignore
                            stickers.stickers |> Dict.iter (fun k v -> sb.Append(k).Append ' ' |> ignore)
                        | _ -> ()
                        sb.ToString())