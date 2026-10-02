using FeatureFlags.Data;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace FeatureFlags.Areas.Identity.Pages.Account;

public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager, ProjectInvitations invitations, ILogger<RegisterModel> logger) : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync(string? invitationToken = null)
    {
        if (string.IsNullOrEmpty(invitationToken)) return;
        try { Input.Email = (await invitations.PreviewAsync(invitationToken, HttpContext.RequestAborted)).Email; }
        catch (ArgumentException ex) { ModelState.AddModelError(string.Empty, ex.Message); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex) { InvitationDiagnostics.LogUnexpected(logger, "registration preview", ex); ModelState.AddModelError(string.Empty, "Unable to load the invitation. Please try again."); }
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, string? invitationToken = null)
    {
        if (!ModelState.IsValid)
            return Page();

        if (!string.IsNullOrEmpty(invitationToken))
        {
            try
            {
                var invitation = await invitations.PreviewAsync(invitationToken, HttpContext.RequestAborted);
                if (ProjectInvitations.NormalizeEmail(Input.Email) != ProjectInvitations.NormalizeEmail(invitation.Email))
                {
                    ModelState.AddModelError("Input.Email", "Use the email address this invitation was sent to.");
                    return Page();
                }
            }
            catch (ArgumentException ex) { ModelState.AddModelError(string.Empty, ex.Message); return Page(); }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return new EmptyResult(); }
            catch (Exception ex) { InvitationDiagnostics.LogUnexpected(logger, "registration preview", ex); ModelState.AddModelError(string.Empty, "Unable to load the invitation. Please try again."); return Page(); }
        }

        var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email };
        var createResult = await _userManager.CreateAsync(user, Input.Password);

        if (createResult.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/projects");
        }

        // Propagate any errors into ModelState
        foreach (var err in createResult.Errors)
            ModelState.AddModelError(string.Empty, err.Description);

        return Page();
    }
    
    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
