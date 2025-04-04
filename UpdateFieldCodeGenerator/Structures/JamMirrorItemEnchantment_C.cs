namespace UpdateFieldCodeGenerator.Structures
{
    [HasChangesMask]
    public class JamMirrorItemEnchantment_C
    {
        //440
        public static readonly UpdateField m_ID = new UpdateField(typeof(int), UpdateFieldFlag.None);
        public static readonly UpdateField m_duration = new UpdateField(typeof(uint), UpdateFieldFlag.None);
        public static readonly UpdateField m_charges = new UpdateField(typeof(short), UpdateFieldFlag.None);
        public static readonly UpdateField m_inactive = new UpdateField(typeof(ushort), UpdateFieldFlag.None);
    }
}
