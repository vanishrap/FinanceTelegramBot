using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FinanceBot.Infrastructure.Persistence;

namespace FinanceBot.Infrastructure.Persistence.Migrations;
[DbContext(typeof(FinanceDbContext))]
public sealed class FinanceDbContextModelSnapshot : ModelSnapshot
{
 protected override void BuildModel(ModelBuilder modelBuilder) { }
}
