using System.Text.Json;
using Workboard.Application.Interfaces;
using Workboard.Domain.Entities;

namespace Workboard.Application.Documents;

public static class DocumentChanges
{
    public static void SaveRevision(ICollaborationRepository repository, ProjectDocument document)
        => repository.AddRevision(new DocumentRevision
        {
            ProjectId = document.ProjectId, DocumentId = document.Id, Version = document.Version,
            Title = document.Title, Body = document.Body, EditorId = document.UpdatedById,
            SavedAtUtc = document.UpdatedAtUtc
        });

    public static void Record(IProjectRepository repository, ProjectDocument document, string action)
    {
        var summary = action switch
        {
            "DocumentCreated" => "Document created.",
            "DocumentUpdated" => "Document updated.",
            "DocumentArchived" => "Document archived.",
            "DocumentRestored" => "Document restored.",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        repository.AddEvent(new ChangeEvent
        {
            ProjectId = document.ProjectId, DocumentId = document.Id, ActorId = document.UpdatedById,
            OccurredAtUtc = document.UpdatedAtUtc, Action = action, Summary = summary,
            DetailsJson = JsonSerializer.Serialize(new { Version = document.Version })
        });
    }
}
