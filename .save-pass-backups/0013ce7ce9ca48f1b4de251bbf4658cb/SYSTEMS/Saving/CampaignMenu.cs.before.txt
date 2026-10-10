// Main-menu campaign actions stay separate from save storage and world loading.
using Godot;
using System;

public partial class MainMenu
{
    private ConfirmationDialog _newCampaignConfirm;

    // =========================================================
    // Confirm replacing this profile's single current-campaign slot on its next save.
    private void NewCampaign()
    {
        if (!CampaignStore.Exists(ProfileStore.Selected.Id)) { OpenCampaign(false); return; }
        if (_newCampaignConfirm == null)
        {
            _newCampaignConfirm = new ConfirmationDialog { Title = "New Campaign" };
            AddChild(_newCampaignConfirm);
            _newCampaignConfirm.Confirmed += () => OpenCampaign(false);
            UIButtonFactory.Apply(_newCampaignConfirm.GetOkButton());
            UIButtonFactory.Apply(_newCampaignConfirm.GetCancelButton());
        }
        _newCampaignConfirm.DialogText = "Start a fresh campaign? Your next Save Game " +
            "will replace this profile's current campaign slot. The existing save stays until then.";
        _newCampaignConfirm.PopupCentered(new Vector2I(540, 220));
    }

    // =========================================================
    // Report validation errors before replacing the menu with a gameplay scene.
    private void OpenCampaign(bool restore)
    {
        try { CampaignSession.Launch(this, restore); }
        catch (Exception error) { ShowMessage("Could not open campaign", error.Message); }
    }
}
