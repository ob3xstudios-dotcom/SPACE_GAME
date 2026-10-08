namespace Game.Map
{
    public static class MapCellIdentity
    {
        public static string ResolveCellId(MapCellDefinition definition, string legacyCellId)
        {
            return Normalize(definition != null ? definition.CellId : legacyCellId);
        }

        public static string ResolveAreaId(MapCellDefinition definition, string legacyAreaId)
        {
            return Normalize(definition != null ? definition.AreaId : legacyAreaId);
        }

        public static string ResolveRoomId(MapCellDefinition definition, string legacyRoomId)
        {
            return Normalize(definition != null ? definition.RoomId : legacyRoomId);
        }

        private static string Normalize(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}
