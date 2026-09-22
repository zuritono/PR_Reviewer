namespace PrReviewer.Domain;

/// <summary>
/// The overall recommendation for the change as a whole.
/// </summary>
public enum ReviewVerdict
{
    Approve,
    ApproveWithComments,
    RequestChanges
}
