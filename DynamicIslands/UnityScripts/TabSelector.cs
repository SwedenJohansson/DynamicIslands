using System;
using UnityEngine;

namespace DynamicIslands.Editor
{

    public class TabSelector : MonoBehaviour
    {
        public TAB SelectedTab = TAB.ObjectPlace;
        public GameObject ToolList;

        /// <summary>Raised after the visible tool panel changes.</summary>
        public event Action<TAB> TabChanged;

        public static TabSelector instance;

        void Start()
        {
            instance = this;
            for (int i = 0; i < ToolList.transform.childCount; i++)
                ToolList.transform.GetChild(i).gameObject.SetActive(i == (int)SelectedTab);
            if (TabChanged != null) TabChanged(SelectedTab);
        }

        public void UpdateTabSelection(int selectedTab)
        {
            if (selectedTab < 0 || selectedTab >= ToolList.transform.childCount) return;
            ToolList.transform.GetChild((int)SelectedTab).gameObject.SetActive(false);
            ToolList.transform.GetChild(selectedTab).gameObject.SetActive(true);

            SelectedTab = (TAB)selectedTab;
            if (TabChanged != null) TabChanged(SelectedTab);
        }
    }

    public enum TAB
    {
        TerrainEdit = 0,
        ObjectPlace = 1
    }

}
