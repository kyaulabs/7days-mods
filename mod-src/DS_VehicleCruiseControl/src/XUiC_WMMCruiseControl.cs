using UnityEngine;

/// <summary>
/// HUD controller for the cruise control bar. Shows Off (grey) / Slow (yellow) / Sprint (green)
/// while the local player is attached to a vehicle; hides otherwise.
/// </summary>
public class XUiC_WMMCruiseControl : XUiController
{
	private XUiV_Label textContent;
	private XUiV_Sprite barContent;
	private EntityPlayerLocal localPlayer;
	private EntityVehicle vehicle;

	public override void Init()
	{
		base.Init();
		base.IsDirty = true;

		XUiController barController = GetChildById("BarContent");
		if (barController != null)
		{
			barContent = barController.ViewComponent as XUiV_Sprite;
		}

		XUiController textController = GetChildById("TextContent");
		if (textController != null)
		{
			textContent = textController.ViewComponent as XUiV_Label;
		}
	}

	public override void Update(float _dt)
	{
		base.Update(_dt);

		if (localPlayer == null && XUi.IsGameRunning())
		{
			localPlayer = xui.playerUI.entityPlayer;
		}

		if (localPlayer != null)
		{
			if (vehicle == null && localPlayer.AttachedToEntity != null && localPlayer.AttachedToEntity is EntityVehicle)
			{
				vehicle = (EntityVehicle)localPlayer.AttachedToEntity;
				base.IsDirty = true;
				// Shift the collected items popup up so the cruise bar doesn't overlap it.
				xui.CollectedItemList.SetYOffset(100);
			}
			else if (vehicle != null && localPlayer.AttachedToEntity == null)
			{
				vehicle = null;
				base.IsDirty = true;
			}
		}

		if (vehicle != null)
		{
			EntityPlayerLocal attachedPlayer = vehicle.GetAttachedPlayerLocal();
			ViewComponent.IsVisible = true;

			float state = ((EntityAlive)attachedPlayer).GetCVar(WMMVehicleCruiseControl.VehicleAutoGo);
			if (state == 2f)
			{
				textContent.Text = "Sprint";
				barContent.Color = new Color32(42, 209, 84, 128);   // green
			}
			else if (state == 1f)
			{
				textContent.Text = "Slow";
				barContent.Color = new Color32(209, 191, 31, 128);  // yellow
			}
			else
			{
				textContent.Text = "Off";
				barContent.Color = new Color32(96, 96, 96, 128);    // grey
			}
		}
		else
		{
			ViewComponent.IsVisible = false;
		}
	}
}
