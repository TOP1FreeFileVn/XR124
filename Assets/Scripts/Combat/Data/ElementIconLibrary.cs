using System;
using UnityEngine;

namespace XR124.Combat
{
    // Biểu tượng và màu nhận diện của 8 hệ, dùng cho HUD pet và UI chọn hệ.
    [CreateAssetMenu(fileName = "ElementIconLibrary", menuName = "XR124/Combat/Element Icon Library", order = 13)]
    public sealed class ElementIconLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ElementType element;
            public Sprite icon;
            public Color color;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        // Gán toàn bộ bảng từ công cụ Editor.
        public void SetEntries(Entry[] newEntries)
        {
            entries = newEntries;
        }

        // Biểu tượng của một hệ; null nếu chưa gán.
        public Sprite GetIcon(ElementType element)
        {
            int index = Find(element);
            return index >= 0 ? entries[index].icon : null;
        }

        // Màu nhận diện của một hệ; trắng nếu chưa gán.
        public Color GetColor(ElementType element)
        {
            int index = Find(element);
            return index >= 0 ? entries[index].color : Color.white;
        }

        // Tìm vị trí mục theo hệ (8 mục nên duyệt tuyến tính là đủ nhanh).
        private int Find(ElementType element)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].element == element)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
