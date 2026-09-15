namespace ProjectManagement.Application.Projects;

public record ProjectSummary(int Id, string Key, string Name, bool IsArchived, bool IsOwner);
public record ProjectHistory(string Actor, DateTime OccurredAtUtc, string Summary);
public record ProjectDetails(int Id, string Key, string Name, string Description, string Owner,
    bool IsArchived, long Version, bool IsOwner, IReadOnlyList<ProjectHistory> History);
public record ProjectParticipant(string UserId, string Email, bool IsOwner, DateTime JoinedAtUtc);
public record ProjectMembers(int ProjectId, string ProjectKey, string ProjectName, bool IsArchived,
    long Version, IReadOnlyList<ProjectParticipant> Participants);
