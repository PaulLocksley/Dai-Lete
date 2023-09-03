using System.Diagnostics;
using System.Net;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Dapper;

namespace Dai_Lete.ScheduledTasks;

public static class PodcastServices
{
    
    public static List<Podcast> GetPodcasts()
    {
        var sql = @"Select * From Podcasts";
        return SqLite.Connection().Query<Podcast>(sql).ToList();
    }

    public static (string guid, string downloadLink) GetLatestEpsiode(Guid podcastId)
    {
        var RssFeed = new XmlDocument();
        var sql = @"SELECT * FROM Podcasts where id = @id";
        Podcast podcast = SqLite.Connection().QueryFirst<Podcast>(sql,new {id = podcastId});
        if (podcast is null)
        {
            throw new FileNotFoundException($"Failed to locate podcast with id {podcastId}");
        }
        try
        {
            using (var reader = XmlReader.Create(podcast.InUri.ToString()))
            {
                RssFeed.Load(reader);
            }
        }
        catch
        {
            throw new Exception($"Failed to parse {podcast.InUri}");
        }
        foreach (XmlElement node in RssFeed.DocumentElement.ChildNodes)
        {
            foreach (XmlElement n2 in node.ChildNodes)
            {
                if (n2.Name != "item")
                {
                    continue;
                }

                XmlNode? guid = null;
                XmlNode? enclosure = null;
                String downloadLink = null;
                foreach (XmlElement n3 in n2.ChildNodes)
                {
                    if (n3.Name == "guid") { guid = n3;}
                    if (n3.Name == "enclosure") { enclosure = n3;}
                }
                foreach (XmlAttribute atr in enclosure.Attributes) {
                    if (atr.Name == "url")
                    {
                        downloadLink = atr.Value;
                        break;
                    }
                }

                return(guid: guid.InnerText,downloadLink:downloadLink);
            }
        }
        throw new FileNotFoundException($"Could not find episode for podcast: {podcastId}");
    }

    public static void downloadEpsisode(Podcast podcast, string episodeUrl)
    {
        //setup clients.
        var remoteHttmpClientHandler = new HttpClientHandler
        {
            Proxy = new WebProxy($"socks5://{ConfigManager.getProxyAddress()}")
        };
        var remoteHttpClient = new HttpClient(remoteHttmpClientHandler);
        var localHttpClient = new HttpClient();
        
        //make folders.
        var workingDirecory = $"{AppDomain.CurrentDomain.BaseDirectory}tmp{Path.DirectorySeparatorChar}";//todo config
        var di = new DirectoryInfo($"{AppDomain.CurrentDomain.BaseDirectory}");
        di.CreateSubdirectory("tmp");
        var destinationLocal = ($"{workingDirecory}{podcast.Id}.local");
        var destinationRemote = ($"{workingDirecory}{podcast.Id}.remote");

        var d1 = localHttpClient.GetByteArrayAsync(episodeUrl).ContinueWith(task =>
        {
            File.WriteAllBytes(destinationLocal, task.Result);
        });

        
        var d2 = remoteHttpClient.GetByteArrayAsync(episodeUrl).ContinueWith(task =>
        {
            File.WriteAllBytes(destinationRemote, task.Result);
        });

        while (!(d1.IsCompleted && d2.IsCompleted)) { Thread.Sleep(100); }
        Console.WriteLine(d2.Status);
        if (d2.IsFaulted || d1.IsFaulted)
        {
            throw new Exception($"{d1.Exception} \n {d2.Exception}");
        }
        Console.WriteLine("Downloads apparently done.");
    }

    public static int processLatest(Guid id, string episodeId)
    {
        var workingDirecory = $"{AppDomain.CurrentDomain.BaseDirectory}tmp{Path.DirectorySeparatorChar}";
        var preLocal = ($"{workingDirecory}{id}.local");
        var workingLocal = $"{preLocal}.wav";
        var preRemote = ($"{workingDirecory}{id}.remote");
        var workingRemote = $"{preLocal}.wav";
        var processedFile = $"{workingDirecory}{id}processed.wav";
        var finalFolder = $"{AppDomain.CurrentDomain.BaseDirectory}Podcasts{Path.DirectorySeparatorChar}{id}";
        var finalFile = $"{finalFolder}{Path.DirectorySeparatorChar}{episodeId}.mp3";
        if (!Directory.Exists(finalFolder))
        {
            DirectoryInfo di = Directory.CreateDirectory(finalFolder);
        }

        string ffmpegArgsL = $" -y -i '{preLocal}'  '{workingLocal}'";
        string ffmpegArgsR = $" -y -i {preRemote}  {workingRemote}";
        string ffmpegArgsFinal = $"-y -i {processedFile} {finalFile}";
        Process process = new Process();
        process.StartInfo.FileName = "ffmpeg";
        process.StartInfo.Arguments = ffmpegArgsL;
        process.EnableRaisingEvents = false;
        process.Start();
        while (!process.HasExited) { Thread.Sleep(100); }
        
        process.StartInfo.Arguments = ffmpegArgsR;
        process.Start();
        while (!process.HasExited) { Thread.Sleep(100); }
        process.Kill();
        byte[] workingL = File.ReadAllBytes(workingLocal); //todo: change to reading from disk
        byte[] workingR = File.ReadAllBytes(workingRemote);
        
        var oneP = 1024;//read all meta data and file headers. 
        var twoP = 1024;
//var zero = new List<Byte> { 0 };
        var byteWindow = 120000;
        var headers = new ArraySegment<Byte>(workingL, 0, 1024).ToList();
        var outBytes = new List<Byte>();
        outBytes.AddRange(headers);
        while (oneP + byteWindow < workingL.Length && twoP + byteWindow < workingR.Length)
        {
            var subArray1 = new ArraySegment<Byte>(workingL, oneP, byteWindow);
            var subArray2 = new ArraySegment<Byte>(workingR, twoP, byteWindow);
            if (Enumerable.SequenceEqual(subArray1, subArray2) || subArray1.All(x => x == 0))
            {
                outBytes.AddRange(subArray1);
                oneP += byteWindow;
                twoP += byteWindow;
                continue;
            }

            for (int i = twoP + 1; i + byteWindow < workingR.Length; i++)//todo: time based processing window.
            {
                subArray2 = new ArraySegment<Byte>(workingR, i, byteWindow);
                if (subArray1.SequenceEqual(subArray2))
                {
                    Console.WriteLine("Found SlidingWindow Match");
                    outBytes.AddRange(subArray1);
                    twoP = i + byteWindow;
                    break;
                }
            }
    
            //if we never find continue.
            oneP += byteWindow;
        }
        Console.WriteLine(outBytes.Count);
        File.WriteAllBytes(processedFile, outBytes.ToArray());
        //one more process :) 
        
        process.StartInfo.Arguments = ffmpegArgsFinal;
        process.Start();
        while (!process.HasExited) { Thread.Sleep(100); }
    
        return (int)new FileInfo(finalFile).Length;
    }
    
}