using ProjectManagement.Application.Common;
namespace ProjectManagement.Application.Projects;

public record ProjectSummary(int Id, string Key, string Name, bool IsArchived, bool IsOwner);
public record ProjectHistory(PersonSummary Actor, DateTime OccurredAtUtc, string Summary);
public record ProjectDetails(int Id, string Key, string Name, string Description, PersonSummary Owner,
    bool IsArchived, long Version, bool IsOwner, IReadOnlyList<ProjectHistory> History);
public record ProjectParticipant(string UserId, string Email, bool IsOwner, DateTime JoinedAtUtc,
    string FirstName = "", string LastName = "")
{
    public PersonSummary Person => new(UserId, FirstName, LastName, Email);
    public string ChoiceLabel => Person.ChoiceLabel;
}
public record ProjectMembers(int ProjectId, string ProjectKey, string ProjectName, bool IsArchived,
    long Version, IReadOnlyList<ProjectParticipant> Participants);
