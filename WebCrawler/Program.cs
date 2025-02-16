using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

class Program
{
    static IMongoClient client;
    static IMongoDatabase database;
    static readonly object lockObj = new object();
    static int id = 1;

    static async Task Main()
    {
        string connectionString = "*************"; // MongoDB connection string
        client = new MongoClient(connectionString);
        database = client.GetDatabase("crawler");

        Console.WriteLine("Choose the operation you want to perform:");
        Console.WriteLine("1. New crawl job");
        Console.WriteLine("2. Purge operation");
        Console.WriteLine("3. Display tree of an existing crawl");
        var operationChoice = int.Parse(Console.ReadLine());
        switch (operationChoice)
        {
            case 1:
                await StartNewCrawl(database);
                break;
            case 2:
                PerformPurgeOperation(client, database);
                break;
            case 3:
                Console.WriteLine("Enter the name of the collection to display tree:");
                var collectionName = Console.ReadLine();
                DisplayTree(database, collectionName);
                break;
            default:
                Console.WriteLine("Invalid choice. Exiting...");
                break;
        }
    }

    static async Task StartNewCrawl(IMongoDatabase database)
    {
        Console.WriteLine("Enter the url of the website that you want to crawl: ");
        var url = Console.ReadLine();
        Console.WriteLine("Enter the depth (1-10): ");
        var depth = int.Parse(Console.ReadLine()) + 1;
        Console.WriteLine("Enter a keyword (leave empty to store all links): ");
        var keyword = Console.ReadLine();

        string collectionName;
        do
        {
            Console.WriteLine("Please write the name of your new collection (the URL itself is recommended): ");
            collectionName = Console.ReadLine();
            if (await CollectionExists(collectionName))
            {
                Console.WriteLine("Collection already exists. Please choose a different name.");
            }
            else
            {
                break;
            }
        } while (true);

        var collection = database.GetCollection<BsonDocument>(collectionName);

        try
        {
            await Crawl(url, depth, "", "", collection, keyword);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
        }
    }

    static async Task<bool> CollectionExists(string collectionName)
    {
        var filter = new BsonDocument("name", collectionName);
        var collections = await database.ListCollectionNames().ToListAsync();
        return collections.Contains(collectionName);
    }

    static async Task Crawl(string url, int depth, string parentId, string label, IMongoCollection<BsonDocument> collection, string keyword)
    {
        if (depth <= 0)
            return;

        if (IsUrlProcessed(url, collection))
            return;

        string currentId;
        lock (lockObj)
        {
            currentId = GetNextId().ToString();
        }

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.AllowAutoRedirect = true;

        try
        {
            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                string html = reader.ReadToEnd();
                var links = GetLinksFromHtml(html, url);

                label = GetLabelFromHtml(html); //gets labels via GetLabelFromHtml

                var document = new BsonDocument
            {
                { "_id", currentId },
                { "parentId", parentId },
                    { "url", url },
                    { "label", label },
                { "childLinks", new BsonArray(links) }
            };

                lock (lockObj)
                {
                    if (!IsUrlProcessed(url, collection))
                    {
                        collection.InsertOne(document);
                    }
                }

                var insertedDocument = collection.Find(new BsonDocument("_id", currentId)).Project(Builders<BsonDocument>.Projection.Exclude("childLinks")).FirstOrDefault();
                if (insertedDocument != null)
                {
                    Console.WriteLine("Inserted Document:");
                    Console.WriteLine(insertedDocument.ToJson());
                }

                Parallel.ForEach(links, link =>  //each element in links becomes link, and each link goes over the Crawl method
                {
                    if (keyword == "" || link.Contains(keyword))
                        Crawl(link, depth - 1, currentId, label, collection, keyword);
                });
            }
        }
        catch (WebException ex)
        {
            if (ex.Response is HttpWebResponse httpResponse)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"404 error occurred: {url}");
                    return;
                }
                else if (httpResponse.StatusCode == HttpStatusCode.Found || httpResponse.StatusCode == HttpStatusCode.Redirect)
                {
                    Console.WriteLine($"Redirected: {url}");
                }
            }
            else
            {
                throw;
            }
        }
    }

    static void PerformPurgeOperation(IMongoClient client, IMongoDatabase database)
    {
        Console.WriteLine("Choose the purge operation you want to perform:");
        Console.WriteLine("1. Purge entire database");
        Console.WriteLine("2. Purge specific collection");
        int purgeChoice = int.Parse(Console.ReadLine());

        if (purgeChoice == 1)
        {
            Console.WriteLine("Are you sure you want to purge the entire database? (yes/no)");
            string confirmation = Console.ReadLine().ToLower();
            if (confirmation == "yes")
            {
                client.DropDatabase(database.DatabaseNamespace.DatabaseName);
                Console.WriteLine("Database purged successfully.");
            }
            else
            {
                Console.WriteLine("Operation cancelled.");
            }
        }
        else if (purgeChoice == 2)
        {
            Console.WriteLine("Enter the name of the collection to purge:");
            string collectionName = Console.ReadLine();
            database.DropCollection(collectionName);
            Console.WriteLine($"Collection '{collectionName}' purged successfully.");
        }
        else
        {
            Console.WriteLine("Invalid choice. Exiting...");
        }
    }

    static bool IsUrlProcessed(string url, IMongoCollection<BsonDocument> collection)
    {
        var filter = Builders<BsonDocument>.Filter.Regex("url", new BsonRegularExpression("^" + Regex.Escape(url.Split('#')[0]) + "$", "i"));
        var result = collection.Find(filter).Any();
        return result;
    }


    static string[] GetLinksFromHtml(string html, string baseUrl)
    {
        MatchCollection matches = Regex.Matches(html, @"<a\s+(?:[^>]*?\s+)?href=([""'])(?!.*?#main)(.*?)\1", RegexOptions.IgnoreCase);
        List<string> links = new List<string>();
        foreach (Match match in matches)
        {
            string href = match.Groups[2].Value; //to get url properly
            if (!href.StartsWith("http"))
            {
                if (href.StartsWith("/")) // it provides a solution to get URLs that do not start with the base url 
                {
                    links.Add(baseUrl.TrimEnd('/') + href);
                }
                else
                {
                    links.Add(baseUrl + href);
                }
            }
            else
            {
                links.Add(href);
            }
        }
        return links.ToArray();
    }

    static string GetLabelFromHtml(string html)
    {
        string label = "";

        Match titleMatch = Regex.Match(html, @"<title[^>]*>\s*(.+?)\s*</title>", RegexOptions.IgnoreCase); // to get the label properly
        if (titleMatch.Success)
        {
            label = WebUtility.HtmlDecode(titleMatch.Groups[1].Value); // it is added to get some values properly - "&amp"
            return label;
        }

        return label;
    }

    static int GetNextId()
    {
        lock (lockObj)
        {
            return id++;
        }
    }

    static void DisplayTree(IMongoDatabase database, string collectionName)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var filter = Builders<BsonDocument>.Filter.Empty;
        var documents = collection.Find(filter).ToList();
        HashSet<string> processedNodes = new HashSet<string>(); // hashset: unordered collection of unique elements

        foreach (var document in documents)
        {
            if (!processedNodes.Contains(document.GetValue("_id").ToString()))
            {
                DisplayTreeNode(document, 0, collection, processedNodes);
            }
        }
    }

    static void DisplayTreeNode(BsonDocument node, int depth, IMongoCollection<BsonDocument> collection, HashSet<string> processedNodes)
    {
        string indent = new string(' ', depth * 4);
        Console.Write($"{indent} - {node.GetValue("_id")}: {node.GetValue("label")} ({node.GetValue("url")})");

        var parentId = node.GetValue("parentId").ToString();
        if (!string.IsNullOrEmpty(parentId))
        {
            Console.WriteLine($" ParentID: {parentId}");
        }
        else Console.WriteLine(" "); //to avoid wrong formation of the first line 

        processedNodes.Add(node.GetValue("_id").ToString());

        var filter = Builders<BsonDocument>.Filter.Eq("parentId", node.GetValue("_id").ToString());
        var childNodes = collection.Find(filter).ToList();

        foreach (var childNode in childNodes)
        {
            if (!processedNodes.Contains(childNode.GetValue("_id").ToString()))
            {
                DisplayTreeNode(childNode, depth + 1, collection, processedNodes);
            }
        }
    }
}