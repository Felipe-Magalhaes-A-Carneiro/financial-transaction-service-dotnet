using FinancialTransactionService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialTransactionService.Application.Common.Interfaces;

/// <summary>
/// Abstração mínima do DbContext exposta para os QueryHandlers da camada Application.
///
/// Design deliberado - por que expor IQueryable aqui e não usar repositórios?
///
/// Repositórios com métodos nomeados (GetByIdAsync, GetByAccountIdAsync) são
/// excelentes para comandos: encapsulam a lógica de busca e retornam entidades ricas que o domínio pode modificar.
///
/// Para queries de leitura, retornar entidades completas e depois mapear para DTOs em memória é ineficiente - o EF Core carrega colunas desnecessárias e pode disparar N+1 via lazy loading. A solução é projetar diretamente para DTOs com .Select() dentro do handler, gerando SQL otimizado com apenas as colunas necessárias e os JOINs corretos.
///
/// Esta interface expõe os DbSet como IQueryable - suficiente para projeções de leitura - sem vazar o DbContext completo para a Application.
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>
    /// Conjunto de transações financeiras para consultas de leitura.
    /// </summary>
    DbSet<Transaction> Transactions { get; }

    /// <summary>
    /// Conjunto de contas financeiras para consultas de leitura.
    /// </summary>
    DbSet<Account> Accounts { get; }
}