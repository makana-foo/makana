namespace Makana.Api.Contracts;

public sealed record CloudBackendReference(
    string StackName,
    string ProjectName,
    string Owner,
    string? DefaultOrganization,
    string? CurrentProjectName)
{
    public string ToFullyQualifiedString()
        => $"{Owner}/{ProjectName}/{StackName}";

    public string ToNameString()
        => StackName;

    public override string ToString()
    {
        var projectMatchesCurrent = !string.IsNullOrWhiteSpace(CurrentProjectName) &&
                                    string.Equals(ProjectName, CurrentProjectName, StringComparison.Ordinal);
        var ownerMatchesDefault = !string.IsNullOrWhiteSpace(DefaultOrganization) &&
                                  string.Equals(Owner, DefaultOrganization, StringComparison.Ordinal);

        if (projectMatchesCurrent && ownerMatchesDefault)
        {
            return StackName;
        }

        if (projectMatchesCurrent)
        {
            return $"{Owner}/{StackName}";
        }

        return $"{Owner}/{ProjectName}/{StackName}";
    }
}
