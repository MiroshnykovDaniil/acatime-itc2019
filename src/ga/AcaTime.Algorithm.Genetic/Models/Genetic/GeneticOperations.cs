using AcaTime.Algorithm.Genetic.Services.Calc;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using AcaTime.ScriptModels;
using Microsoft.Extensions.Logging;

namespace AcaTime.Algorithm.Genetic.Models.Genetic;

public class GeneticOperations
{
    
    internal ILogger logger;

    // public FacultySeasonDTO Root { get; set; }
    // public UserFunctions UserFunctions { get; set; }
    // public AlgorithmParams Parameters { get; internal set; }
    
    public Individual timetable { get; set; }
    
    public List<CombinedIndividual> population { get; set; }
    
    private List<CombinedIndividual> newGeneration { get; set; }
    
    public void Setup(ILogger logger, Individual timetable)
    {
        // Root = root;
        this.logger = logger;
        // UserFunctions = userFunctions;
        // Parameters = parameters;
        this.timetable = timetable;
        
        population = new List<CombinedIndividual>();
        newGeneration = new List<CombinedIndividual>();

        СreateIndividual(timetable); // створити першу особу
    }
    
    #region Основні операції
    private readonly Random _random = new();


    public void СreateIndividual(Individual individual)
    {
        
        IndividualGenes individualGenes = new IndividualGenes();
        
        CombinedIndividual combinedIndividual = new CombinedIndividual();
        
        combinedIndividual.Setup(individual, individualGenes);

        combinedIndividual.currentEstimation = combinedIndividual.Estimate();

        if (!population.Contains(combinedIndividual))
        {
            population.Add(combinedIndividual);
        }
        
        // combinedIndividual = new CombinedIndividual();
        // combinedIndividual.Setup(individual, individualGenes);
        // combinedIndividual.currentEstimation = combinedIndividual.Estimate();
        // population.Add(combinedIndividual);
    }

    public void ApplyGenes(CombinedIndividual combinedIndividual, IndividualGenes individualGenes)
    {
        
        combinedIndividual.Setup(timetable, individualGenes);

        if (!population.Contains(combinedIndividual))
        {
            population.Add(combinedIndividual);
        }
        
        // population[combinedIndividual.id] = combinedIndividual;
    }

    public void MakeOperation()
    {
        foreach (var combinedIndividual in population)
        {
            PopulationMutationsForShortSeries(combinedIndividual);
            combinedIndividual.currentEstimation = combinedIndividual.Estimate();
        }
    }
    
    
    private HashSet<int> usedSeries = new HashSet<int>();

    private void PopulationMutationsForShortSeries(CombinedIndividual individual)
    {
        var clonedIndividual = new CombinedIndividual(); // змінити ось тут на клонування з урахуванням змін
        clonedIndividual.Setup(timetable, individual.genes);
        
        clonedIndividual.currentEstimation = individual.currentEstimation;
        var mutatedSeriesDomain = clonedIndividual.Mutations(individual.currentEstimation, -1, 3, usedSeries);
        if(mutatedSeriesDomain != null)
            usedSeries.Add(mutatedSeriesDomain.Value.Key);
        if(clonedIndividual.currentEstimation >= individual.currentEstimation)
            newGeneration.Add(clonedIndividual);
        if (individual != population[0] && 
            individual.currentEstimation < population[0].currentEstimation && 
            clonedIndividual.currentEstimation > individual.currentEstimation &&
            clonedIndividual.currentEstimation < population[0].currentEstimation
           )
        {
            logger.LogInformation($"Намагаємось перенести зміни на кращий розклад");
            if (mutatedSeriesDomain != null)
            {
                // population[0].ApplyMutation((KeyValuePair<int, DomainValue>)mutatedSeriesDomain);
            }
        }
    }


    
    
    
    #endregion
}