#nullable enable

namespace M2V.Editor.GUI
{
    public enum Language
    {
        English,
        Japanese
    }

    internal static class Localization
    {
        internal static string Get(Language language, string key)
        {
            return language == Language.Japanese
                ? GetJapanese(key)
                : GetEnglish(key);
        }

        private static string GetEnglish(string key)
        {
            return key switch
            {
                Keys.Title => "M2V",
                Keys.Subtitle => "Java World Importer",
                Keys.TabWorlds => "Worlds",
                Keys.TabRange => "Range",
                Keys.TabGenerate => "Generate",
                Keys.WorldsTitle => "Worlds",
                Keys.WorldTip => "Tip: Drag & drop a world folder here",
                Keys.Reload => "Reload",
                Keys.SelectCustomFolder => "Select Custom Folder...",
                Keys.Next => "Next",
                Keys.Back => "Back",
                Keys.RangeTitle => "Range",
                Keys.RangeMin => "Min",
                Keys.RangeMax => "Max",
                Keys.RangeDimension => "Dimension",
                Keys.BlockScale => "Block Scale",
                Keys.RangeHint => "Tip: You can edit values directly or adjust around spawn.",
                Keys.DimensionOverworld => "Overworld",
                Keys.DimensionNether => "Nether",
                Keys.DimensionEnd => "End",
                Keys.Clear => "Clear",
                Keys.GenerateTitle => "Generate",
                Keys.GenerateHint => "Run import and mesh generation for the selected world.",
                Keys.OpenFolder => "Open Folder",
                Keys.PlayCaption => "Ready to build",
                Keys.GenerateButton => "GENERATE",
                Keys.SummaryWorld => "World",
                Keys.SummaryRange => "Range",
                Keys.SummaryDimension => "Dimension",
                Keys.SummaryScale => "Scale",
                Keys.SummaryPacks => "Packs",
                Keys.SummaryNone => "None",
                Keys.SummaryResource => "Resource",
                Keys.SummaryData => "Data",
                Keys.SummaryResourceData => "Resource + Data",
                Keys.ModeSuffix => "Mode",
                Keys.VersionLabel => "Version",
                Keys.StatusNoFolder => "No folder selected.",
                Keys.StatusValid => "World folder looks valid.",
                Keys.StatusInvalid => "Invalid folder. Missing level.dat.",
                Keys.DialogSelectWorld => "Please select a valid Minecraft world folder.",
                Keys.DialogEnterRange => "Please enter valid range values.",
                Keys.DialogJarMissing => "Minecraft version jar not found for this world.",
                Keys.DialogMeshFailed => "Mesh generation failed.",
                Keys.LoadingTitle => "Loading...",
                Keys.LoadingPreparing => "Preparing...",
                Keys.LoadingReadingBlocks => "Reading blocks…",
                Keys.LoadingGeneratingMesh => "Generating mesh…",
                Keys.LoadingApplyingMaterial => "Applying material…",
                Keys.DialogTitle => "Minecraft2VRChat",
                _ => key
            };
        }

        private static string GetJapanese(string key)
        {
            return key switch
            {
                Keys.Title => "M2V",
                Keys.Subtitle => "Java ワールドインポーター",
                Keys.TabWorlds => "ワールド",
                Keys.TabRange => "範囲",
                Keys.TabGenerate => "生成",
                Keys.WorldsTitle => "ワールド",
                Keys.WorldTip => "ヒント: ワールドフォルダをドラッグ&ドロップ",
                Keys.Reload => "再読み込み",
                Keys.SelectCustomFolder => "フォルダを選択...",
                Keys.Next => "次へ",
                Keys.Back => "戻る",
                Keys.RangeTitle => "範囲",
                Keys.RangeMin => "最小",
                Keys.RangeMax => "最大",
                Keys.RangeDimension => "ディメンション",
                Keys.BlockScale => "ブロックスケール",
                Keys.RangeHint => "ヒント: 数値を直接入力するか、スポーン周辺に調整できます。",
                Keys.DimensionOverworld => "オーバーワールド",
                Keys.DimensionNether => "ネザー",
                Keys.DimensionEnd => "エンド",
                Keys.Clear => "クリア",
                Keys.GenerateTitle => "生成",
                Keys.GenerateHint => "選択したワールドを読み込み、メッシュを生成します。",
                Keys.OpenFolder => "フォルダを開く",
                Keys.PlayCaption => "生成の準備完了",
                Keys.GenerateButton => "生成",
                Keys.SummaryWorld => "ワールド",
                Keys.SummaryRange => "範囲",
                Keys.SummaryDimension => "ディメンション",
                Keys.SummaryScale => "スケール",
                Keys.SummaryPacks => "パック",
                Keys.SummaryNone => "なし",
                Keys.SummaryResource => "リソース",
                Keys.SummaryData => "データ",
                Keys.SummaryResourceData => "リソース + データ",
                Keys.ModeSuffix => "モード",
                Keys.VersionLabel => "バージョン",
                Keys.StatusNoFolder => "フォルダが未選択です。",
                Keys.StatusValid => "ワールドフォルダは有効です。",
                Keys.StatusInvalid => "無効なフォルダです。level.dat が見つかりません。",
                Keys.DialogSelectWorld => "有効なMinecraftワールドフォルダを選択してください。",
                Keys.DialogEnterRange => "範囲の値を正しく入力してください。",
                Keys.DialogJarMissing => "このワールドに対応するMinecraftのjarが見つかりません。",
                Keys.DialogMeshFailed => "メッシュ生成に失敗しました。",
                Keys.LoadingTitle => "読み込み中...",
                Keys.LoadingPreparing => "準備中...",
                Keys.LoadingReadingBlocks => "ブロックを読み込み中…",
                Keys.LoadingGeneratingMesh => "メッシュ生成中…",
                Keys.LoadingApplyingMaterial => "マテリアル適用中…",
                Keys.DialogTitle => "Minecraft2VRChat",
                _ => key
            };
        }

        internal static class Keys
        {
            internal const string Title = "title";
            internal const string Subtitle = "subtitle";
            internal const string TabWorlds = "tab.worlds";
            internal const string TabRange = "tab.range";
            internal const string TabGenerate = "tab.generate";
            internal const string WorldsTitle = "worlds.title";
            internal const string WorldTip = "worlds.tip";
            internal const string Reload = "action.reload";
            internal const string SelectCustomFolder = "action.select_custom";
            internal const string Next = "action.next";
            internal const string Back = "action.back";
            internal const string RangeTitle = "range.title";
            internal const string RangeMin = "range.min";
            internal const string RangeMax = "range.max";
            internal const string RangeDimension = "range.dimension";
            internal const string BlockScale = "range.block_scale";
            internal const string RangeHint = "range.hint";
            internal const string DimensionOverworld = "dimension.overworld";
            internal const string DimensionNether = "dimension.nether";
            internal const string DimensionEnd = "dimension.end";
            internal const string Clear = "action.clear";
            internal const string GenerateTitle = "generate.title";
            internal const string GenerateHint = "generate.hint";
            internal const string OpenFolder = "action.open_folder";
            internal const string PlayCaption = "generate.ready";
            internal const string GenerateButton = "generate.button";
            internal const string SummaryWorld = "summary.world";
            internal const string SummaryRange = "summary.range";
            internal const string SummaryDimension = "summary.dimension";
            internal const string SummaryScale = "summary.scale";
            internal const string SummaryPacks = "summary.packs";
            internal const string SummaryNone = "summary.none";
            internal const string SummaryResource = "summary.resource";
            internal const string SummaryData = "summary.data";
            internal const string SummaryResourceData = "summary.resource_data";
            internal const string ModeSuffix = "meta.mode";
            internal const string VersionLabel = "meta.version";
            internal const string StatusNoFolder = "status.no_folder";
            internal const string StatusValid = "status.valid";
            internal const string StatusInvalid = "status.invalid";
            internal const string DialogTitle = "dialog.title";
            internal const string DialogSelectWorld = "dialog.select_world";
            internal const string DialogEnterRange = "dialog.enter_range";
            internal const string DialogJarMissing = "dialog.jar_missing";
            internal const string DialogMeshFailed = "dialog.mesh_failed";
            internal const string LoadingTitle = "loading.title";
            internal const string LoadingPreparing = "loading.preparing";
            internal const string LoadingReadingBlocks = "loading.reading_blocks";
            internal const string LoadingGeneratingMesh = "loading.generating_mesh";
            internal const string LoadingApplyingMaterial = "loading.applying_material";
        }
    }
}
