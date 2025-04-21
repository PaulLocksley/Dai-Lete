using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Dapper;
using Microsoft.AspNetCore.Connections;

namespace Dai_Lete.Services;

public static class PodcastServices
{
    private static readonly ILogger<ConvertNewEpisodes> _logger = new Logger<ConvertNewEpisodes>(new LoggerFactory());
 
    
    public static List<Podcast> GetPodcasts()
    {
        var sql = @"Select * From Podcasts";
        var results = SqLite.Connection().Query<Podcast>(sql);
        return results.ToList();
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

                string? guid = null;
                XmlNode? enclosure = null;
                String downloadLink = null;
                foreach (XmlElement n3 in n2.ChildNodes)
                {
                    if (n3.Name == "guid")
                    {
                        guid = string.Concat(n3.InnerText.Split(Path.GetInvalidFileNameChars()));
                    }

                    if (n3.Name == "enclosure") { enclosure = n3;}
                }
                foreach (XmlAttribute atr in enclosure.Attributes) {
                    if (atr.Name == "url")
                    {
                        downloadLink = atr.Value;
                        break;
                    }
                }
                if (downloadLink is null || guid is null)
                {
                    throw new Exception($"Failed to parse {podcast.InUri}");
                }
                return(guid: guid,downloadLink:downloadLink);
            }
        }
        throw new FileNotFoundException($"Could not find episode for podcast: {podcastId}");
    }

    public static void DownloadEpisode(Podcast podcast, string episodeUrl,string episodeGuid)
    {
        //setup clients.
        var remoteHttpClientHandler = new HttpClientHandler
        {
            Proxy = new WebProxy($"socks5://{ConfigManager.getProxyAddress()}")
            /*AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10*/
            
        };
        var remoteHttpClient = new HttpClient(remoteHttpClientHandler);
        var localHttpClient = new HttpClient();
        localHttpClient.DefaultRequestHeaders.Add("User-Agent","Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:15.0) Gecko/20100101 Firefox/15.0.1" ); 
        remoteHttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:15.0) Gecko/20100101 Firefox/15.0.1"); 
        
        
        
        //make folders.
        var workingDirectory = $"{AppDomain.CurrentDomain.BaseDirectory}tmp{Path.DirectorySeparatorChar}";//todo config
        var di = new DirectoryInfo($"{AppDomain.CurrentDomain.BaseDirectory}");
        di.CreateSubdirectory("tmp");
        if (!Directory.Exists($"{workingDirectory}{podcast.Id}"))
        {
            Directory.CreateDirectory($"{workingDirectory}{podcast.Id}");
        }

        workingDirectory = $"{workingDirectory}{podcast.Id}{Path.DirectorySeparatorChar}";
        var destinationLocal = ($"{workingDirectory}{episodeGuid}.local");
        var destinationRemote = ($"{workingDirectory}{episodeGuid}.remote");
        try
        {
            var d1 = localHttpClient.GetByteArrayAsync(episodeUrl).ContinueWith(task =>
            {
                _logger.LogInformation("local download started");
                if (task.IsFaulted)
                {
                    Console.WriteLine($"Local download failed: {task.Exception}");
                }
                File.WriteAllBytes(destinationLocal, task.Result);
            });
            var d2 = remoteHttpClient.GetByteArrayAsync(episodeUrl).ContinueWith(task =>
            {
                _logger.LogInformation("remote download started");
                if (task.IsFaulted)
                {
                    Console.WriteLine($"remote download failed: {task.Exception}");
                }
                File.WriteAllBytes(destinationRemote, task.Result);
            });


                d1.Wait();
                d2.Wait();
                if (d1.Status != TaskStatus.RanToCompletion || d2.Status != TaskStatus.RanToCompletion)
            {
                throw new Exception($"{d1.Exception} \n {d2.Exception}");
            }

            _logger.LogInformation("Downloads apparently done.");
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Download failed of {episodeUrl}\n{e}");
            throw;
        }
    }

    public static int ProcessDownloadedEpisode(Guid id, string episodeId)
    {
        _logger.LogInformation($"Starting to process {episodeId}");
        var workingDirectory = $"{AppDomain.CurrentDomain.BaseDirectory}tmp{Path.DirectorySeparatorChar}{id}{Path.DirectorySeparatorChar}";
        var preLocal = ($"{workingDirectory}{episodeId}.local");
        var workingLocal = $"{preLocal}.wav";
        var preRemote = ($"{workingDirectory}{episodeId}.remote");
        var workingRemote = $"{preLocal}.wav";
        var processedFile = $"{workingDirectory}{episodeId}processed.wav";
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
        while (!process.HasExited)
        {
            Thread.Sleep(100);
        }

        process.StartInfo.Arguments = ffmpegArgsR;
        process.Start();
        while (!process.HasExited)
        {
            Thread.Sleep(100);
        }

        process.Kill();
        var workingL = File.OpenRead(workingLocal);
        var workingR = File.OpenRead(workingRemote);
        var workingLLength = new FileInfo(workingLocal).Length;
        var workingRLength = new FileInfo(workingRemote).Length;
        var outStream = File.Create(processedFile);

        var oneP = 1024; //read hopefully all meta data and file headers. 
        var twoP = oneP;
        workingR.Seek(oneP, SeekOrigin.Begin);
        var byteWindow = 120000;

        var headers = new byte[oneP];
        workingL.Read(headers, 0, oneP);
        outStream.Write(headers);

        while (workingL.Position + byteWindow < workingLLength && workingR.Position + byteWindow < workingRLength)
        {
            var bufferL = new byte[byteWindow];
            var bufferR = new byte[byteWindow];
            var initialR = workingR.Position;
            workingL.Read(bufferL, 0, byteWindow);
            workingR.Read(bufferR, 0, byteWindow);
            if (bufferL.SequenceEqual(bufferR) || bufferL.All(x => x == 0))
            {
                outStream.Write(bufferL);
                continue;
            }

            for (long i = initialR + 1; i + byteWindow < workingRLength; i++)//todo: time based window match
            {
                workingR.Seek(i, SeekOrigin.Begin);
                workingR.Read(bufferR, 0, byteWindow);
                if (bufferL.SequenceEqual(bufferR))
                {
                    outStream.Write(bufferL);
                    break;
                }
            }

            workingR.Seek(initialR, SeekOrigin.Begin);
        }
        outStream.Close();
        workingL.Close();
        workingR.Close();
        //one more process :) 
        
        process.StartInfo.Arguments = ffmpegArgsFinal;
        process.Start();
        while (!process.HasExited) { Thread.Sleep(100); }
        process.Kill();
        
        File.Delete(preLocal);
        File.Delete(workingLocal);
        File.Delete(preRemote);
        File.Delete(workingRemote);
        File.Delete(processedFile);
        
        _logger.LogInformation($"Completed processing {episodeId}");
        return (int)new FileInfo(finalFile).Length;
    }
    
}