using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AssetStudio;
namespace AssetStudioGUI
{
    internal partial class ExportAnimatorClipsForm : Form
    {
        private List<AssetItem> allClips;
        private List<AssetItem> linkedClips;
        public List<AssetItem> SelectedClips { get; private set; }
        private bool isInitialLoad = true;
        public ExportAnimatorClipsForm(AssetItem animatorItem, List<AssetItem> projectAssets)
        {
            InitializeComponent();
            
            // Get linked clips
            var animator = (Animator)animatorItem.Asset;
            var linkedClipAssets = GetLinkedAnimationClips(animator, projectAssets);
            
            this.linkedClips = linkedClipAssets;
            this.allClips = projectAssets.Where(x => x.Type == ClassIDType.AnimationClip).ToList();
            
            lblPrompt.Text = $"Select the Animation Clips to export with: {animatorItem.Text}";
            
            // Populate list
            PopulateClipsList();
        }
        
        private List<AssetItem> GetLinkedAnimationClips(Animator animator, List<AssetItem> projectAssets)
        {
            var linkedList = new List<AnimationClip>();
            if (animator.m_Controller.TryGet(out var m_Controller))
            {
                AnimatorController animatorController = null;
                if (m_Controller is AnimatorOverrideController overrideController)
                {
                    // Overrides
                    foreach (var clipOverride in overrideController.m_Clips)
                    {
                        if (clipOverride.m_OverrideClip.TryGet(out var overrideClip))
                        {
                            linkedList.Add(overrideClip);
                        }
                        else if (clipOverride.m_OriginalClip.TryGet(out var originalClip))
                        {
                            linkedList.Add(originalClip);
                        }
                    }
                    overrideController.m_Controller.TryGet(out animatorController);
                }
                else
                {
                    animatorController = m_Controller as AnimatorController;
                }
                if (animatorController != null)
                {
                    foreach (var pptr in animatorController.m_AnimationClips)
                    {
                        if (pptr.TryGet(out var clip))
                        {
                            linkedList.Add(clip);
                        }
                    }
                }
            }
            // Also from GameObject / Animation components
            if (animator.m_GameObject.TryGet(out var gameObject))
            {
                CollectFromGameObject(gameObject, linkedList);
            }
            // Distinct list of AnimationClips
            var distinctClips = linkedList.Distinct(new AnimationClip.EqComparer()).ToList();
            
            // Match distinctClips to project AssetItem elements
            var linkedAssets = new List<AssetItem>();
            foreach (var clip in distinctClips)
            {
                var match = projectAssets.FirstOrDefault(x => x.Asset == clip);
                if (match != null)
                {
                    linkedAssets.Add(match);
                }
            }
            return linkedAssets;
        }
        private void CollectFromGameObject(GameObject gameObject, List<AnimationClip> animationList)
        {
            if (gameObject.m_Animation != null)
            {
                foreach (var animation in gameObject.m_Animation.m_Animations)
                {
                    if (animation.TryGet(out var animationClip))
                    {
                        animationList.Add(animationClip);
                    }
                }
            }
            if (gameObject.m_Transform != null)
            {
                foreach (var childPtr in gameObject.m_Transform.m_Children)
                {
                    if (childPtr.TryGet(out var childTransform) && childTransform.m_GameObject.TryGet(out var childGameObject))
                    {
                        CollectFromGameObject(childGameObject, animationList);
                    }
                }
            }
        }
        private void PopulateClipsList()
        {
            var checkedAssets = new HashSet<AssetItem>();
            if (isInitialLoad)
            {
                foreach (var clip in linkedClips)
                {
                    checkedAssets.Add(clip);
                }
                isInitialLoad = false;
            }
            else
            {
                for (int i = 0; i < lstClips.Items.Count; i++)
                {
                    if (lstClips.GetItemChecked(i) && lstClips.Items[i] is ClipItem item)
                    {
                        checkedAssets.Add(item.AssetItem);
                    }
                }
            }
            lstClips.Items.Clear();
            var filter = txtSearch.Text.Trim();
            
            var listToUse = chkShowAll.Checked ? allClips : linkedClips;
            
            foreach (var clipAsset in listToUse)
            {
                if (!string.IsNullOrEmpty(filter) && clipAsset.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                
                var isLinked = linkedClips.Contains(clipAsset);
                var item = new ClipItem { AssetItem = clipAsset, IsLinked = isLinked };
                
                var isChecked = checkedAssets.Contains(clipAsset);
                lstClips.Items.Add(item, isChecked);
            }
        }
        private void btnSelectAll_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < lstClips.Items.Count; i++)
            {
                lstClips.SetItemChecked(i, true);
            }
        }
        private void btnSelectNone_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < lstClips.Items.Count; i++)
            {
                lstClips.SetItemChecked(i, false);
            }
        }
        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            PopulateClipsList();
        }
        private void chkShowAll_CheckedChanged(object sender, EventArgs e)
        {
            PopulateClipsList();
        }
        private void btnOK_Click(object sender, EventArgs e)
        {
            SelectedClips = new List<AssetItem>();
            foreach (var checkedItem in lstClips.CheckedItems)
            {
                if (checkedItem is ClipItem item)
                {
                    SelectedClips.Add(item.AssetItem);
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
    internal class ClipItem
    {
        public AssetItem AssetItem { get; set; }
        public bool IsLinked { get; set; }
        public override string ToString()
        {
            return AssetItem.Text + (IsLinked ? " (Linked)" : "");
        }
    }
}
