using System.Text.RegularExpressions;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dapper;

namespace Dai_Lete.Services;

public class XmlService
{
    private readonly ILogger<XmlService> _logger;
    private readonly ConfigManager _configManager;
    private readonly RedirectService _redirectService;
    private readonly IDatabaseService _databaseService;

    public XmlService(ILogger<XmlService> logger, ConfigManager configManager, RedirectService redirectService, IDatabaseService databaseService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _redirectService = redirectService ?? throw new ArgumentNullException(nameof(redirectService));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    public async Task<XmlDocument> GenerateNewFeedAsync(Guid podcastId)
    {
        try
        {
            _logger.LogInformation("Generating new feed for podcast {PodcastId}", podcastId);

            var rssFeed = new XmlDocument();
            string metaDataName = string.Empty;
            Uri? metaDataImageUrl = null;
            string metaDataAuthor = string.Empty;
            string metaDataDescription = string.Empty;
            var processedEpisodes = new List<PodcastEpisodeMetadata>();
            var nonProcessedEpisodes = new List<PodcastEpisodeMetadata>();
            bool hasItunesBlock = false;

            using var connection = await _databaseService.GetConnectionAsync();

            const string podcastSql = @"SELECT * FROM Podcasts WHERE Id = @podcastId";
            var podcast = await connection.QueryFirstOrDefaultAsync<Podcast>(podcastSql, new { podcastId });

            if (podcast is null)
            {
                throw new ArgumentException($"Podcast with ID {podcastId} not found", nameof(podcastId));
            }

            const string episodesSql = @"SELECT Id, FileSize FROM Episodes WHERE PodcastId = @pid";
            var episodesQuery = await connection.QueryAsync(episodesSql, new { pid = podcastId });
            var episodes = episodesQuery.ToDictionary(
                row => (string)row.Id,
                row => (int)row.FileSize
            );

            using var reader = XmlReader.Create(podcast.InUri.ToString());
            rssFeed.Load(reader);

            await ProcessPreProcessingInstructionsAsync(rssFeed, $"{podcast.InUri.Scheme}://{podcast.InUri.Host}" +
                                                               (podcast.InUri.IsDefaultPort ? "" : $":{podcast.InUri.Port}"));
            var root = rssFeed.DocumentElement;

            foreach (XmlElement node in root.ChildNodes)
            {
                foreach (XmlElement item in node.ChildNodes)
                {
                    if (item.Name != "item")
                    {
                        switch (item.Name)
                        {
                            case "title":
                                metaDataName = item.InnerText;
                                item.InnerText = $"[DAI-LETE] {item.InnerText}";
                                break;
                            case "description":
                                metaDataDescription = item.InnerText;
                                break;
                            case "itunes:author":
                                metaDataAuthor = item.InnerText;
                                break;
                            case "itunes:image":
                                if (Uri.TryCreate(item.Attributes?.GetNamedItem("href")?.InnerText, UriKind.Absolute, out var imageUri))
                                {
                                    metaDataImageUrl = imageUri;
                                }
                                break;
                            case "itunes:new-feed-url":
                                item.ParentNode?.RemoveChild(item);
                                break;
                            case "itunes:block":
                                item.InnerText = "yes";
                                hasItunesBlock = true;
                                break;
                        }
                        continue;
                    }

                    string? guid = null;
                    XmlNode? enclosure = null;

                    foreach (XmlElement element in item.ChildNodes)
                    {
                        switch (element.Name)
                        {
                            case "guid":
                                guid = string.Concat(element.InnerText.Split(Path.GetInvalidFileNameChars()));
                                break;
                            case "enclosure":
                                enclosure = element;
                                break;
                        }
                    }

                    if (string.IsNullOrEmpty(guid)) continue;

                    if (episodes.ContainsKey(guid))
                    {
                        processedEpisodes.Add(GetEpisodeMetaData(item, podcast));

                        if (enclosure?.Attributes != null)
                        {
                            foreach (XmlAttribute attribute in enclosure.Attributes)
                            {
                                switch (attribute.Name)
                                {
                                    case "url":
                                        attribute.Value = $"https://{_configManager.GetBaseAddress()}/Podcasts/{podcastId}/{guid}.mp3";
                                        break;
                                    case "length":
                                        attribute.Value = episodes[guid].ToString();
                                        break;
                                    case "type":
                                        attribute.Value = "audio/mpeg";
                                        break;
                                }
                            }
                        }
                    }
                    else
                    {
                        nonProcessedEpisodes.Add(GetEpisodeMetaData(item, podcast));
                    }
                }

                if (!hasItunesBlock)
                {
                    var itunesBlockElement = rssFeed.CreateElement("itunes", "block", "http://www.itunes.com/dtds/podcast-1.0.dtd");
                    itunesBlockElement.InnerText = "yes";
                    node.AppendChild(itunesBlockElement);
                }
            }

            FeedCache.updateMetaData(podcastId, new PodcastMetadata(metaDataName, metaDataAuthor,
                                                                    metaDataImageUrl, metaDataDescription,
                                                                    processedEpisodes, nonProcessedEpisodes));

            _logger.LogInformation("Successfully generated feed for podcast {PodcastId}", podcastId);
            return rssFeed;
        }
        catch (Exception ex) when (!(ex is ArgumentException))
        {
            _logger.LogError(ex, "Failed to generate feed for podcast {PodcastId}", podcastId);
            throw new InvalidOperationException($"Failed to generate feed for podcast {podcastId}", ex);
        }
    }


    private static PodcastEpisodeMetadata GetEpisodeMetaData(XmlElement podcastEpisode, Podcast podcast)
    {
        var pm = new PodcastEpisodeMetadata(podcast);
        foreach (XmlElement node in podcastEpisode.ChildNodes)
        {
            switch (node.Name)
            {
                case "title":
                    pm.episodeName = node.InnerText;
                    break;
                case "description":
                    pm.description = node.InnerText;
                    break;
                case "guid":
                    pm.episodeId = string.Concat(node.InnerText.Split(Path.GetInvalidFileNameChars()));
                    break;
                case "pubDate":
                    pm.pubDate = DateTime.Parse(node.InnerText);
                    break;
                case "itunes:image":
                    if (Uri.TryCreate(node.Attributes?.GetNamedItem("href")?.InnerText, UriKind.Absolute, out var imageUri))
                    {
                        pm.imageLink = imageUri;
                    }
                    break;
                case "enclosure":
                    if (Uri.TryCreate(node.Attributes?.GetNamedItem("url")?.InnerText, UriKind.Absolute, out var downloadUri))
                    {
                        pm.downloadLink = downloadUri;
                    }
                    break;
            }
        }
        return pm;
    }

    private async Task ProcessPreProcessingInstructionsAsync(XmlDocument xmlDocument, string baseUri)
    {
        if (xmlDocument is null) throw new ArgumentNullException(nameof(xmlDocument));
        if (string.IsNullOrWhiteSpace(baseUri)) throw new ArgumentException("Base URI cannot be null or empty", nameof(baseUri));

        try
        {
            const string matchPattern = @"(.*href=[""'])(\/.+)([""'].*)";

            foreach (XmlNode node in xmlDocument.ChildNodes)
            {
                if (node is not XmlProcessingInstruction || string.IsNullOrEmpty(node.Value))
                {
                    continue;
                }

                var match = Regex.Match(node.Value, matchPattern);
                if (!match.Success) continue;

                var url = baseUri + match.Groups[2].Value;
                var redirectLink = await _redirectService.GetOrCreateRedirectLinkAsync(url);
                if (string.IsNullOrEmpty(redirectLink.Id))
                {
                    throw new InvalidOperationException($"Failed to generate redirect ID for URL: {url}");
                }
                _logger.LogDebug("Created redirect link for {Url} with ID {Id}", url, redirectLink.Id);
                node.Value = $"{match.Groups[1].Value}/redirect?id={redirectLink.Id}\" ";
                //dumping this for now. &{match.Groups[3].Value}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process preprocessing instructions for base URI: {BaseUri}", baseUri);
            throw;
        }
    }
}