namespace VgcCollege.MVC.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId
    {
        get
        {
            if (string.IsNullOrEmpty(RequestId))
            {
                return false;
            }
            return true;
        }
    }
}