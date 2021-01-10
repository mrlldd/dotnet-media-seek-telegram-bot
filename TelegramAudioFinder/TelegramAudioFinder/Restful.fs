module TelegramAudioFinder.Restful

open System.Text
open Suave
open TelegramAudioFinder.Utils


let rest handler (req: HttpRequest) =
    req.rawForm
    |> Encoding.UTF8.GetString
    |> fromJson
    |> handler

let restful handler = rest handler |> request
