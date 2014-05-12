open MongoDB.Bson
open MongoDB.Bson.IO
open MongoDB.Driver
open MongoDB.Driver.Builders
open MongoDB.FSharp
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Files
open Suave.Http.Successful
open Suave.Http.RequestErrors
open Suave.Types
open Suave.Utils
open Suave.Utils.Option
open Suave.Web
open System
open System.Collections.Generic
open System.Text.RegularExpressions

Serializers.Register()

let client = MongoClient("mongodb://localhost")
let server = client.GetServer()
let db = server.GetDatabase("idelo")

let jsonSettings = JsonWriterSettings(OutputMode = JsonOutputMode.Strict)

let plain_request f (a : HttpContext) =
    a.request.response.Headers.Add("Content-Type", "text/plain")
    f a.request a

let ApplySort (mappings : string -> string) (query : Dictionary<string, string>) (values : MongoCursor<'T>) =
    let sortField = query ? order |> or_default "" |> mappings
    let sortBy = match query ? direction with
                 | Some "asc" -> SortBy.Ascending(sortField)
                 | _ -> SortBy.Descending(sortField)
    values.SetSortOrder(sortBy)

let ApplyPaging (query : Dictionary<string, string>) (values : MongoCursor<'T>) =
    let offset = match query ? fromrow with
                 | Some x -> let mutable n = 0
                             match Int32.TryParse(x, &n) with
                             | true -> n
                             | _ -> 0
                 | _ -> 0
    let count = match query ? getrows with
                | Some x -> let mutable n = 0
                            match Int32.TryParse(x, &n) with
                            | true -> n
                            | _ -> 100
                | _ -> 100
    values.SetSkip(offset).SetLimit(count)
                
let result_response (total : int64) (items : BsonValue list) =
    let document = BsonDocument([BsonElement("total", (BsonInt64 total)); BsonElement("items", (BsonArray items))])
    OK (document.ToJson jsonSettings)

(*
let GetSubjects = plain_request (fun req ->
    let collection = db.GetCollection citizenCollection
    let result = collection.Aggregate(BsonDocument("$unwind", "$complaints")
                                      BsonDocument("$group", BsonDocument(BsonElement("_id", BsonDocument(BsonElement("_id", BsonString "$_id")
                                                                                                          BsonElement("name", BsonString "$name")
                                                                                                          BsonElement("gender", BsonString "$gender")
                                                                                                          BsonElement("birth_date", BsonString "$birth_date")
                                                                                                          BsonElement("address", BsonString "$address")
                                                                                                          BsonElement("photo_uri", BsonString "$photo_uri"))),
                                                                          BsonElement("count", BsonDocument("$sum", BsonInt32 1))))
                                      BsonDocument("$project", BsonDocument(BsonElement("_id", BsonString "$_id._id")
                                                                            BsonElement("name", BsonString "$_id.name")
                                                                            BsonElement("gender", BsonString "$_id.gender")
                                                                            BsonElement("birth_date", BsonString "$_id.birth_date")
                                                                            BsonElement("address", BsonString "$_id.address")
                                                                            BsonElement("photo_uri", BsonString "$_id.photo_uri")
                                                                            BsonElement("count", BsonInt32 1)))
                                      BsonDocument("$sort", BsonElement("count", BsonInt32 1)))
    
    (*
db.complaint.aggregate(
/*    {$project: {
        title: 1,
        created_at: 1,
        tags: 1
    }},*/
    {$unwind: "$tags"},
    {$group: {_id: { title: "$title", created_at: "$created_at" }, count: { $sum: 1 } }},
    {$sort: {count: 1}}
).result
    *)

    let values = collection.FindAll()
    let count = values.Clone().Count()
    let subjects = values |> GetFields [ "name"; "gender"; "birth_date"; "address"; "photo_uri", "complaints" ]
)
    *)

// let fields = [ Simple("title"); Simple("subject"); Simple("time"); Simple("lat"); Simple("lng"); Array("tags", ','); Simple("description"); Simple("user") ]

let parseDouble str =
    let mutable d = 0.0
    match Double.TryParse(str, &d) with
    | true -> Some d
    | _ -> None

let parseInt str =
    let mutable n = 0
    match Int32.TryParse(str, &n) with
    | true -> Some n
    | _ -> None

let parseDateTime str =
    let mutable dt = DateTime.MinValue
    match DateTime.TryParse(str, &dt) with
    | true -> Some dt
    | _ -> None

module Subject =
    [<Literal>]
    let collectionName = "subjects"

    let mappings = function
        | "name" -> "Name"
        | "gender" -> "Gender"
        | "birth_date" -> "BirthDate"
        | "address" -> "Address"
        | "photo_uri" -> "PhotoUri"
        | _ -> "_id"

    type Complaint = { Id : ObjectId
                       Title : string
                       Time : DateTime
                       Latitude : double
                       Longitude : double
                       Tags : string []
                       Description : string
                       User : ObjectId
                       Photos : string []
                       CreatedAt : DateTime }

    type Subject = { Id : ObjectId
                     Name : string
                     Gender : string
                     BirthDate : DateTime
                     Address : string
                     PhotoUri : string
                     Complaints : Complaint [] }

    let Create = plain_request (fun req ->
        match req.query ? name, req.query ? gender, req.query ? birth_date with
        | Some name, Some gender, Some birth_date ->
            let subject = { Id = ObjectId.GenerateNewId()
                            Name = name
                            Gender = gender
                            BirthDate = parseDateTime birth_date |> or_default DateTime.Today
                            Address = match req.query ? address with | Some adr -> adr | _ -> ""
                            PhotoUri = match req.query ? photo_uri with | Some uri -> uri | _ -> ""
                            Complaints = [||] }
            let collection = db.GetCollection<Subject> collectionName
            collection.Insert(subject) |> ignore
            OK (subject.Id.ToJson jsonSettings)
        | _ -> OK "null"
    )

    let Find = plain_request (fun req ->
        let criterias = seq { match req.query ? id with
                              | Some id -> yield Query<Subject>.EQ((fun x -> x.Id), ObjectId id)
                              | _ -> ()
                              match req.query ? name with
                              | Some name -> yield Query<Subject>.Matches((fun x -> x.Name), BsonRegularExpression(name, "i"))
                              | _ -> ()
                              match req.query ? gender with
                              | Some gender -> yield Query<Subject>.EQ((fun x -> x.Gender), gender)
                              | _ -> () }
                        |> Seq.toList
        let collection = db.GetCollection<Subject> collectionName
        let values = match criterias with
                     | [] -> collection.FindAll()
                     | _ -> collection.Find(Query.And(criterias))
        let numSubjects = values.Clone().Count();
        let subjects = values |> ApplyPaging req.query |> ApplySort mappings req.query
        result_response numSubjects (subjects |> Seq.map (fun s -> s.ToBsonDocument() :> BsonValue) |> Seq.toList)
    )

    let FindComplaint = plain_request (fun req ->
        let direction query =
            match query ? direction with
            | Some "asc" -> 1
            | _ -> -1
        let mappings = function
            | "title" -> "Complaints.Title"
            | "date" -> "Complaints.Time"
            | "subject" -> "Name"
            | _ -> "Complaints._id"
        let searchCriteria = [
            yield BsonDocument("$unwind", BsonString "$Complaints")
            match req.query ? user with
            | Some user -> yield BsonDocument("$match", BsonDocument("Complaints.User", BsonObjectId (ObjectId user)))
            | _ -> ()
        ]
        let countCriteria = [
            yield BsonDocument("$group", BsonDocument([BsonElement("_id", BsonNull.Value)
                                                       BsonElement("count", BsonDocument("$sum", BsonInt32 1))]))
        ]
        let viewCriteria = [
            match req.query ? order |> or_default "" |> mappings with
            | "Complaints._id" -> yield BsonDocument("$sort", BsonDocument("Complaints._id", BsonInt32 (direction req.query)))
            | orderBy -> yield BsonDocument("$sort", BsonDocument([BsonElement(orderBy, BsonInt32 (direction req.query))
                                                                   BsonElement("Complaints._id", BsonInt32 -1)]))
            match req.query ? fromrow with
            | Some n -> let skipFrom = parseInt n
                        if skipFrom.IsSome then
                            yield BsonDocument("$skip", BsonInt32 skipFrom.Value)
            | _ -> ()
            match req.query ? getrows with
            | Some n -> let numRows = parseInt n
                        if numRows.IsSome then
                            yield BsonDocument("$limit", BsonInt32 numRows.Value)
            | _ -> ()
        ]
        let collection = db.GetCollection collectionName
        let numComplaints = collection.Aggregate(AggregateArgs(Pipeline = List.append searchCriteria countCriteria)) |> Seq.head
        let complaints = collection.Aggregate(AggregateArgs(Pipeline = List.append searchCriteria viewCriteria))
        result_response (numComplaints.["count"].AsInt32 |> int64) (complaints |> Seq.map (fun x -> x :> BsonValue) |> Seq.toList)
    )

    let MakeComplaint = plain_request (fun req ->
        match req.query ? subject with
        | Some subjectId ->
            let collection = db.GetCollection<Subject> collectionName
            let subject = collection.FindOne(Query<Subject>.EQ((fun sub -> sub.Id), ObjectId subjectId))
            match subject |> box with
            | null -> OK "null"
            | _ ->
                match req.query ? title, req.query ? time, req.query ? lat, req.query ? lng, req.query ? tags, req.query ? description, req.query ? user with
                | Some title, Some time, Some lat, Some lng, Some tags, Some description, Some user ->
                    let tagArray = tags.Split(',')
                                   |> Array.map (fun str -> str.Trim())
                                   |> Array.filter (fun str -> not (String.IsNullOrWhiteSpace(str)))
                    let photos = req.query.Keys
                                 |> Seq.filter (fun k -> Regex.IsMatch(k, @"^photo[1-5]$") && not (String.IsNullOrWhiteSpace(req.query.[k])))
                                 |> Seq.map (fun k -> req.query.[k])
                                 |> Seq.toArray
                    let complaint = { Id = ObjectId.GenerateNewId()
                                      Title = title
                                      Time = parseDateTime time |> or_default DateTime.Now
                                      Latitude = parseDouble lat |> or_default 0.0
                                      Longitude = parseDouble lng |> or_default 0.0
                                      Tags = tagArray
                                      Description = description
                                      User = ObjectId user
                                      Photos = photos
                                      CreatedAt = DateTime.Now }
                    collection.Save({ subject with Complaints = subject.Complaints |> Array.append [| complaint |] }) |> ignore
                    OK (complaint.Id.ToJson jsonSettings)
                | _ -> OK "null"
        | _ -> OK "null"
    )

    let OfUser = plain_request (fun req ->
        match req.query ? user with
        | Some id ->
            let collection = db.GetCollection<Subject> collectionName
            let userCriteria = Query.EQ("Complaints.User", BsonObjectId (ObjectId id))
            let criteria = match req.query ? tags with
                           | Some str -> Query.And(userCriteria, Query.All("Complaints.Tags", (str.Split(',') |> Array.map (fun s -> BsonString (s.Trim()) :> BsonValue))))
                           | _ -> userCriteria
            let cursor = collection.Find(criteria)
            let numSubjects = cursor.Clone().Count()
            let subjects = cursor |> ApplyPaging req.query |> ApplySort mappings req.query
            result_response numSubjects (subjects
                                         |> Seq.map (fun s -> let doc = s.ToBsonDocument()
                                                              doc.Remove("Complaints")
                                                              doc :> BsonValue)
                                         |> Seq.toList)
        | _ -> result_response 0L []
    )

    let Tags = plain_request (fun req ->
        let criterias = seq {
            yield BsonDocument("$project", BsonDocument("tags", BsonString "$Complaints.Tags"))
            yield BsonDocument("$unwind", BsonString "$tags")
            yield BsonDocument("$unwind", BsonString "$tags")
            yield BsonDocument("$group", BsonDocument("_id", BsonString "$tags"))
            yield BsonDocument("$project", BsonDocument("insensitive", BsonDocument("$toLower", BsonString "$_id")))
            match req.query ? q with
            | Some q -> yield BsonDocument("$match", BsonDocument("insensitive", BsonRegularExpression(q.ToLower())))
            | _ -> ()
            yield BsonDocument("$sort", BsonDocument("insensitive", BsonInt32 1))
            yield BsonDocument("$project", BsonDocument("_id", BsonInt32 1))
            yield BsonDocument("$limit", BsonInt32 (parseInt (req.query ? count |> or_default "") |> or_default 10))
        }
        let collection = db.GetCollection collectionName
        OK (collection.Aggregate(AggregateArgs(Pipeline = criterias)).ToJson jsonSettings)
    )

module User =
    [<Literal>]
    let collectionName = "users"

    type User = { Id : ObjectId
                  Email : string
                  Password : string
                  Name : string
                  Role : string
                  CreatedAt : DateTime }

    let Auth = plain_request (fun req ->
        match req.query ? email, req.query ? password with
        | Some email, Some password ->
            let collection = db.GetCollection<User> collectionName
            let user = collection.FindOne(Query.And(Query<User>.EQ((fun x -> x.Email), email),
                                                    Query<User>.EQ((fun x -> x.Password), password)))
            match user |> box with
            | null -> OK "null"
            | _ -> OK (user.ToJson jsonSettings)
        | _ -> OK "null"
    )

    let Exists = plain_request (fun req ->
        match req.query ? email with
        | Some email ->
            let collection = db.GetCollection<User> collectionName
            let user = collection.FindOne(Query<User>.EQ((fun x -> x.Email), email))
            match user |> box with
            | null -> OK "0"
            | _ -> OK (user.Id.ToJson jsonSettings)
        | _ -> OK "0"
    )

    let Register = plain_request (fun req ->
        match req.query ? email, req.query ? password with
        | Some "", _ | _, Some "" -> OK "0"
        | Some email, Some password ->
            let collection = db.GetCollection<User> collectionName
            let user = { Id = ObjectId.GenerateNewId()
                         Email = email
                         Password = password
                         Name = ""
                         Role = ""
                         CreatedAt = DateTime.Now }
            collection.Insert user |> ignore
            OK (user.Id.ToJson jsonSettings)
        | _ -> OK "0"
    )

let ideloApp : WebPart =
    choose [ GET >>= url "/Subject/Create" >>= Subject.Create
             GET >>= url "/Subject/Find" >>= Subject.Find
             GET >>= url "/Subject/FindComplaint" >>= Subject.FindComplaint
             GET >>= url "/Subject/MakeComplaint" >>= Subject.MakeComplaint
             GET >>= url "/Subject/OfUser" >>= Subject.OfUser
             GET >>= url "/Subject/Tags" >>= Subject.Tags
             GET >>= url "/User/Auth" >>= User.Auth
             GET >>= url "/User/Exists" >>= User.Exists
             GET >>= url "/User/Register" >>= User.Register
             GET >>= browse
             NOT_FOUND "Found no handlers" ]

let default_mime_types_map = function
    | ".map"
    | ".eot"
    | ".ttf"  -> Some { name = "application/octet-stream"; compression = true }
    | ".svg"  -> Some { name = "image/svg+xml"; compression = true }
    | ".woff" -> Some { name = "application/font-woff"; compression = true }
    | ".css"  -> Some { name = "text/css"; compression = true }
    | ".htm"  -> Some { name = "text/html"; compression = true }
    | ".js"   -> Some { name = "application/x-javascript"; compression = true }
    | _       -> None

let ideloConfig = { default_config with
                        mime_types_map = default_mime_types_map
                        home_folder = Some "/home/janno/Work/Idelo" }

web_server ideloConfig ideloApp
