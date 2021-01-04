open System.Threading
open Suave
open System
open Suave.Filters
open Suave.Successful
open Suave.Operators
let isCtrlC (consoleKeyInfo: ConsoleKeyInfo) =
    consoleKeyInfo.Key = ConsoleKey.C
    && consoleKeyInfo.Modifiers = ConsoleModifiers.Control

let rec untilCloseCombination closeCombination =
    if not (Console.ReadKey true |> closeCombination)
    then untilCloseCombination closeCombination

let defaultHandler (r: HttpRequest) =
    OK(String.Format("well met {0}", r.path))
    
[<EntryPoint>]
let main _ =
    let cts = new CancellationTokenSource()

    let conf =
        { defaultConfig with
              cancellationToken = cts.Token }
    printfn "Starting up..."

    let webPath =
        choose [ path "/" >=> (OK "Bruh, here we go again")
                 path "/api/telegram"
                 >=> POST
                 >=> (OK "there should be telegram message handler")
                 request defaultHandler ]

    let _, server = startWebServerAsync conf webPath

    Async.Start(server, cts.Token)
    untilCloseCombination isCtrlC
    printfn "Shutting down..."
    cts.Cancel(false)
    0 // return an integer exit code
