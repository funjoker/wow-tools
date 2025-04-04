namespace UpdateFieldCodeGenerator.Structures
{
    [HasChangesMask]
    [HasMutableInterface]
    public class JamMirrorVisibleItem_C
    {
        //440
        public static readonly UpdateField m_itemID = new UpdateField(typeof(int), UpdateFieldFlag.None);
        public static readonly UpdateField m_itemAppearanceModID = new UpdateField(typeof(ushort), UpdateFieldFlag.None);
        public static readonly UpdateField m_itemVisual = new UpdateField(typeof(ushort), UpdateFieldFlag.None);
    }
}
