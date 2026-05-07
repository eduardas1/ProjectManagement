using System.Text.Json;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Attachments;

public static class AttachmentChanges
{
    public static void Record(IProjectRepository repository, Attachment attachment, string actorId, DateTime now, string action)
    {
        var summary = action switch
        {
            "AttachmentUploaded" => "Attachment uploaded.",
            "AttachmentRemoved" => "Attachment removed.",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        repository.AddEvent(new ChangeEvent
        {
            ProjectId = attachment.ProjectId, WorkItemId = attachment.WorkItemId, DocumentId = attachment.DocumentId,
            ActorId = actorId, OccurredAtUtc = now, Action = action, Summary = summary,
            DetailsJson = JsonSerializer.Serialize(new { AttachmentId = attachment.Id })
        });
    }
}
