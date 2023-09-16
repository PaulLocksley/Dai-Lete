using System.Data;
using System.Security.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.ScheduledTasks;
using Dai_Lete.Services;
using Dapper;

namespace Dai_Lete.Controllers;
[ApiController]
[Route("[controller]")]
public class PodcastController : Controller
{
    [HttpPost("add")]
    public Guid addPodcast(Uri inUri, string authToken)
    {
        if (ConfigManager.getAuthToken() != authToken )
        {
            throw new AuthenticationException("Not authorised");
        }
        
        var p = new Podcast(inUri);
        var sql = @"INSERT INTO Podcasts (inuri,outuri,id)  VALUES (@InUri,@OutUri,@Id)";
        Console.WriteLine($"obj: {p.InUri.ToString()} , {p.OutUri},{p.Id}");
        var rows = SqLite.Connection().Execute(sql, new {InUri = p.InUri.ToString(), OutUri = p.OutUri, Id = p.Id});
        if (rows != 1)
        {
            throw new DataException();
        }

        return p.Id;
    }
    
    [HttpGet("list-podcasts")]
    [Produces("application/json", "application/xml")]
    public IActionResult listPodcasts()
    {
        return Ok(PodcastServices.GetPodcasts().Select(x => x.ToString())); //todo: Work out how to setup default serialization because this is stupid.
    }

    [HttpGet("podcast-feed")]
    [Produces("application/xml")]
    public ContentResult getFeed(Guid id)
    {
        if (!FeedCache.feedCache.ContainsKey(id))
        {
            throw new Exception($"Failed to find podcast with key: {id}");
        }
        return Content(FeedCache.feedCache[id].OuterXml, "application/xml");
        //return Content(XmlService.GenerateNewFeed(id).OuterXml, "application/xml");
    }
    
}