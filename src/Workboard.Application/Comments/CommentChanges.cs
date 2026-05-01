using System.Text.Json;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Comments;

public static class CommentChanges
{
    public static void Record(IProjectRepository repository, Comment comment, string actorId, DateTime now, string action)
    {
        var summary = action switch
        {
            "CommentCreated" => "Comment added.",
            "CommentUpdated" => "Comment updated.",
            "CommentRemoved" => "Comment removed.",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        repository.AddEvent(new ChangeEvent
        {
            ProjectId = comment.ProjectId, WorkItemId = comment.WorkItemId, ActorId = actorId,
            OccurredAtUtc = now, Action = action, Summary = summary,
            DetailsJson = JsonSerializer.Serialize(new { CommentId = comment.Id })
        });
    }
}
