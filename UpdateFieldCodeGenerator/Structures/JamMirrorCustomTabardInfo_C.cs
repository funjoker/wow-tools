namespace UpdateFieldCodeGenerator.Structures
{
    [HasChangesMask(blockGroupSize: -1)]
    public class JamMirrorCustomTabardInfo_C
    {
        //440
        public static readonly UpdateField m_emblemStyle = new UpdateField(typeof(int), UpdateFieldFlag.None);
        public static readonly UpdateField m_emblemColor = new UpdateField(typeof(int), UpdateFieldFlag.None);
        public static readonly UpdateField m_borderStyle = new UpdateField(typeof(int), UpdateFieldFlag.None);
        public static readonly UpdateField m_borderColor = new UpdateField(typeof(int), UpdateFieldFlag.None);
        public static readonly UpdateField m_backgroundColor = new UpdateField(typeof(int), UpdateFieldFlag.None);
    }
}
