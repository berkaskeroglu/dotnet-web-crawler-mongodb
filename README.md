# Web Crawler with MongoDB Integration 🌐🕷️

A sophisticated web crawler that stores hierarchical website data in MongoDB with parallel processing capabilities.

## Features ✨
- Multi-depth website crawling (1-10 levels)
- MongoDB storage with collection management
- Parent-child relationship tracking
- Keyword filtering for links
- Tree structure visualization
- Database purge operations
- Parallel crawling implementation
- HTTP error handling (404/redirects)
- HTML title extraction for labeling

## Prerequisites 🔧
- .NET 6+ SDK
- MongoDB Atlas cluster or local instance
- Modern IDE (VS/Rider/VSCode)

## Setup & Configuration ⚙️

1. Clone repository:
```bash
git clone https://github.com/yourusername/WebCrawlerWithMongoDB.git
```
2. Update MongoDB connection:
```bash
// In Program.cs
string connectionString = "mongodb+srv://<user>:<password>@cluster0.x6b5uvi.mongodb.net/";
```
3. Install dependencies:
```bash
dotnet restore
```

##Usage
```bash
dotnet run

Choose operation:
1. New crawl job
2. Purge operation
3. Display tree

Sample workflow:
1. Enter URL: https://example.com
2. Depth: 3
3. Keyword: "blog"
4. Collection name: example_com_crawl
```

##ID System
```bash
static int id = 1;
static readonly object lockObj = new object();

int GetNextId() {
    lock (lockObj) { return id++; }
}
```

##Tree Display Logic
```bash
void DisplayTreeNode(BsonDocument node, int depth) {
    string indent = new string(' ', depth * 4);
    Console.WriteLine($"{indent} - {node["_id"]}: {node["label"]}");
}
```
