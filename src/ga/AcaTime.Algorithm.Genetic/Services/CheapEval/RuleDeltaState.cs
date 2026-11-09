using AcaTime.ScriptModels;

namespace AcaTime.Algorithm.Genetic.Services.CheapEval;

/// <summary>
/// Стан декомпозиції одного правила для одного індивідуума.
/// RunningTotal завжди дорівнює повній оцінці правила на поточному стані,
/// поки Enabled=true (інваріант підтримується інкрементально і перевіряється періодично).
/// </summary>
public class RuleDeltaState
{
    public long RuleId;
    public string Name = "";
    public Func<IFacultySeason, int> Script = null!;

    /// <summary>null => правило не декомпонується, завжди повна оцінка</summary>
    public RuleFamily? Family;

    public int AllEmpty;
    public Dictionary<long, int> Baselines = new();
    public Dictionary<(long unit, DateTime date), int> Cells = new();
    public long RunningTotal;
    public bool Enabled => Family != null;

    /// <summary>
    /// Memo-кеш клітинок: (unit, date) -> (композит хешів годівників, значення).
    /// Композит = xor хешів кошиків (gs, date) усіх gs юніта — повний видимий
    /// вхід скрипта клітинки. Спрацьовує при undo-черті сканів (той самий
    /// контент => скрипт не викликається). null у probe/temp станах.
    /// </summary>
    public Dictionary<(long unit, DateTime date), (ulong hash, int value)>? Memo;
}
