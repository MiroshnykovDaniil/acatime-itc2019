using AcaTime.Algorithm.Genetic.Models.Genetic;

namespace AcaTime.Algorithm.Genetic.Services.Operations;

/// <summary>
/// Провайдер генетичних операторів (семантичний шар предметної області).
/// Ядро GA (GeneticScheduleAlgorithmUnit) викликає Run(...); конкретна
/// реалізація визначає набір операторів та їх поведінку.
/// Поточна реалізація для задачі AcaTime — SeriesOperations (поведінка
/// збережена 1:1). ITC-адаптер додається окремо й не змінює цей шлях.
/// </summary>
public interface IGeneticOperations
{
    /// <summary>Виконати оператор з номером operation над individual.</summary>
    void Run(int operation, Individual individual, List<Individual> output);
}
