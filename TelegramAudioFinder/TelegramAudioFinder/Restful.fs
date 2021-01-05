module TelegramAudioFinder.Restful

open System.Text
open Suave.Http
open Utils


let rest handler (req: HttpRequest) =
    req.rawForm
    |> Encoding.UTF8.GetString
    |> fromJson
    |> handler
    
let restful handler = request (rest handler)
