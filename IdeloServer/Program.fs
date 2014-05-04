open MongoDB.Bson
open MongoDB.Bson.IO
open MongoDB.Driver
open MongoDB.Driver.Builders
open MongoDB.FSharp
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Types
open Suave.Utils
open Suave.Utils.Option
open Suave.Web
open System
open System.IO
open System.Text

Serializers.Register()

let client = new MongoClient("mongodb://localhost")
let server = client.GetServer()
let db = server.GetDatabase("idelo")

let tables = [| 0 .. 4 |]
             |> Array.map (fun n -> sprintf "t%d" n)

let fields = [| 0 .. 19 |]
             |> Array.map (fun n -> sprintf "f%d" n)

let GetCollectionName (req : HttpRequest) =
    match req.query ? table with
    | Some name -> tables |> Array.tryFind (fun x -> x = name)
    | _ -> None

let Otsi = request (fun req ->
    req.response.Headers.Add("Content-Type", "text/plain")
    match GetCollectionName(req) with
    | None -> OK "0"
    | Some tableName ->
        let collection = db.GetCollection tableName
        let fieldQuery = Query.Or(fields |> Array.map (fun x -> Query.Exists(x)))
        let queries = fields |> Array.fold (fun acc f -> match req.query.ContainsKey f with
                                                         | true -> Query.EQ(f, new BsonString(req.query.[f])) :: acc
                                                         | _ -> acc) []
        let values = match queries with
                     | [] -> collection.Find(fieldQuery)
                     | _ -> collection.Find(Query.And(fieldQuery :: queries))
        let sortField =
            let sortName = req.query ? order |> or_default ""
            match fields |> Array.tryFind (fun f -> f = sortName) with
            | Some x -> x
            | _ -> "_id"
        let sortBy = match req.query ? direction with
                     | Some "asc" -> SortBy.Ascending(sortField)
                     | _ -> SortBy.Descending(sortField)
        let offset = match req.query ? fromrow with
                     | Some x -> let mutable n = 0
                                 match Int32.TryParse(x, &n) with
                                 | true -> n
                                 | _ -> 0
                     | _ -> 0
        let count = match req.query ? getrows with
                    | Some x -> let mutable n = 0
                                match Int32.TryParse(x, &n) with
                                | true -> n
                                | _ -> 100
                    | _ -> 100
        let settings = new JsonWriterSettings(OutputMode = JsonOutputMode.Strict)
        OK (values.SetFields(Fields.Include(fields)).SetSkip(offset).SetLimit(count).SetSortOrder(sortBy).ToJson(settings))
)

let Salvesta = request (fun req ->
    req.response.Headers.Add("Content-Type", "text/plain")
    match GetCollectionName(req) with
    | None -> OK "0"
    | Some tableName ->
        let collection = db.GetCollection tableName
        let elements = fields |> Array.fold (fun acc f -> match req.query.ContainsKey(f) && not (String.IsNullOrWhiteSpace(req.query.[f])) with
                                                          | true -> new BsonElement(f, new BsonString(req.query.[f])) :: acc
                                                          | _ -> acc) []
        match elements |> List.isEmpty with
        | true -> OK "0"
        | _ ->
            collection.Insert(new BsonDocument(elements)) |> ignore
            OK "1"
)

let Uuenda = request (fun req ->
    req.response.Headers.Add("Content-Type", "text/plain")
    match GetCollectionName(req) with
    | None -> OK "0"
    | Some tableName ->
        let collection = db.GetCollection tableName
        let hasFields = fields |> Array.exists (fun x -> req.query.ContainsKey(x) && not (String.IsNullOrWhiteSpace(req.query.[x])))
        match req.query ? id, hasFields with
        | Some id, true ->
            let update = fields |> Array.fold (fun (acc : UpdateBuilder) f -> match req.query.ContainsKey(f) && not (String.IsNullOrWhiteSpace(req.query.[f])) with
                                                                              | true -> acc.Set(f, new BsonString(req.query.[f]))
                                                                              | _ -> acc.Unset(f)) (new UpdateBuilder())
            collection.Update(Query.EQ("_id", new BsonObjectId(new ObjectId(id))), update) |> ignore
            OK "1"
        | _ -> OK "0"
)

let ideloApp : WebPart =
    choose [
        GET >>= url "/Otsi" >>= Otsi
        GET >>= url "/Salvesta" >>= Salvesta
        GET >>= url "/Uuenda" >>= Uuenda
        NOT_FOUND "Found no handlers"
    ]

web_server default_config ideloApp
