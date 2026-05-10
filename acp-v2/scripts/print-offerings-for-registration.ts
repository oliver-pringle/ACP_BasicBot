import { OFFERINGS } from "../src/offerings/registry.js";
import { priceFor } from "../src/pricing.js";

function main() {
  const names = Object.keys(OFFERINGS).sort();
  if (names.length === 0) {
    console.log("(no offerings registered)");
    return;
  }
  for (const name of names) {
    const offering = OFFERINGS[name]!;
    const price = priceFor(name);
    console.log("=".repeat(72));
    console.log(`Offering name:        ${offering.name}`);
    console.log(`Price:                ${price.amount} ${price.token}`);
    console.log(`SLA:                  ${offering.slaMinutes} min  (estimated max time from hire to deliverable)`);
    console.log(`Description:`);
    console.log(`  ${offering.description}`);
    console.log(`Requirement schema (JSON):`);
    console.log(JSON.stringify(offering.requirementSchema, null, 2));
    console.log(`Example request (JSON):`);
    console.log(JSON.stringify(offering.requirementExample, null, 2));
    console.log(`Deliverable schema (JSON):`);
    console.log(JSON.stringify(offering.deliverableSchema, null, 2));
    console.log(`Example deliverable (JSON):`);
    console.log(JSON.stringify(offering.deliverableExample, null, 2));
    console.log("");
  }
  console.log("=".repeat(72));
  console.log(`Total: ${names.length} offering(s).`);
  console.log(`Paste each block into app.virtuals.io → BasicBot agent → Offerings → New offering.`);
  console.log(`The marketplace form takes the requirement schema. Deliverable schema + example are for`);
  console.log(`offering descriptions, buyer docs, and pre-launch wire-shape validation.`);
}

main();
