using AcaTime.Algorithm.Genetic.Models.Genetic;

namespace AcaTime.Algorithm.Genetic.Services.Operations;

/// <summary>
/// Поточний набір операторів AcaTime (series-семантика). Це ПЕРЕМІЩЕНИЙ
/// 1:1 switch з GeneticScheduleAlgorithmUnit.GeneticAlgorithm: жодних змін
/// порядку, умов чи RNG. Типово використовується завжди (Adapter=series).
/// </summary>
internal sealed class SeriesOperations : IGeneticOperations
{
    private readonly GeneticScheduleAlgorithmUnit _unit;

    public SeriesOperations(GeneticScheduleAlgorithmUnit unit)
    {
        _unit = unit;
    }

    public void Run(int operation, Individual individual, List<Individual> output)
    {
        switch (operation)
        {
            // цикл 1:2:1:1:3 — ОПТИМУМ знайдено bake-off'ами 20260830/31:
            // chain 4/8 = +17066 (голод на long-series), 3/8 = +29108-30416.
            case 0:
            case 6:
            case 7:
                _unit.PopulationChainRelocate(individual, output);
                break;
            case 3:
                _unit.PopulationMutationsForShortSeries(individual, output);
                break;
            case 1:
            case 4:
                _unit.PopulationMutationsForLongSeries(individual, output);
                break;
            case 2:
                _unit.PopulationSwapGroupSubjects(individual, output);
                break;
            case 5:
                _unit.PopulationDestroyRepair(individual, output);
                break;
            case 8:
                // HOTSPOT-RELOCATE: перший цільовий op (рушій вказує
                // найгарячіші клітинки — generic через декомпозицію)
                _unit.PopulationHotspotRelocate(individual, output);
                break;
            case 9:
                // ITC-CLASS-MUTATE: скан доменів класу без low-days фільтра
                // (1-заняттєві серії в ITC-кодуванні завжди IsLowDaysDanger)
                _unit.PopulationClassMutationsItc(individual, output);
                break;
            case 10:
                // ITC-DOUBLE-CLASS-MUTATE: два послідовні класові скани
                _unit.PopulationDoubleClassMutationItc(individual, output);
                break;
            case 11:
                // ITC-EVICT-CHAIN: виселення блокувальників + жадібне повернення
                _unit.PopulationEvictChainItc(individual, output);
                break;
            case 12:
                // ITC-ROOM-EVICT: batch of room-aware/eviction-chain targets
                _unit.PopulationRoomAwareEvictChainItc(individual, output);
                break;
            default:
                _unit.PopulationChainRelocate(individual, output);
                break;
        }
    }
}
