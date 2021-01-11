module MediaSeekBot.Search.SoundCloud

open System.Net
open MediaSeekBot
open MediaSeekBot.Utils
open MediaSeekBot.Services.Utils
open FSharp.Data
open FSharp.Json


type SoundCloudTrack =
    { ArtworkUrl: string option
      Title: string
      Description: string option
      PermalinkUrl: string
      PlaybackCount: int }

type SoundCloudSearchResult = { Collection: SoundCloudTrack list }

type private ISoundCloudClient =
    abstract searchTracks: arg:string * int -> Async<Result<seq<UnifiedSearchResult>, exn>>


let private soundCloudTrackMapper (track: SoundCloudTrack): UnifiedSearchResult =
    { Url = track.PermalinkUrl
      ThumbUrl = track.ArtworkUrl
      Title = track.Title
      Description = track.Description
      PlayCount = track.PlaybackCount |> uint64 }

let private soundCloudClient =
    let template =
        sprintf
            "https://api-v2.soundcloud.com/search/tracks?facet=genre&client_id=%s&limit=%i&q=%s&offset=%i"
            applicationConfig.Services.SoundCloud.ClientId
            pagePerService

    let config =
        JsonConfig.create (jsonFieldNaming = Json.snakeCase)
    { new ISoundCloudClient with
        member x.searchTracks(q: string, offset: int) =
            async {
                let url = template q offset
                let! response = url |> Http.AsyncRequestString |> Async.Catch

                return
                    response
                    |> Result.ofChoice
                    |> Result.bind (fun success ->
                        success
                        |> Json.deserializeEx<SoundCloudSearchResult> config
                        |> (fun x -> x.Collection)
                        |> Seq.map soundCloudTrackMapper
                        |> Ok)
            } }

let soundCloudSearch (tup: ServiceSearchArgument): Async<ServiceSearchResult> =
    let (offset, inlineQuery, _) = tup
    soundCloudClient.searchTracks (inlineQuery.Query, offset)
