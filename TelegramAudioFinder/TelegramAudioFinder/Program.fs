module TelegramAudioFinder.Program

open System
open Suave
open Operators
open TelegramAudioFinder
open Startup
open Utils


let isCtrlC (consoleKeyInfo: ConsoleKeyInfo) =
    consoleKeyInfo.Key = ConsoleKey.C
    && consoleKeyInfo.Modifiers = ConsoleModifiers.Control

let rec untilCloseCombination closeCombination =
    if not (Console.ReadKey true |> closeCombination)
    then untilCloseCombination closeCombination

[<EntryPoint>]
let main _ =
    let _, server = startupAsync cts.Token

    Async.Start(server, cts.Token)
    untilCloseCombination isCtrlC
    printfn "Shutting down..."
    cts.Cancel(false)
    0
