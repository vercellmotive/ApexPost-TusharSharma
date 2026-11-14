@api
Feature: Shipment creation
  As a back-office administrator at an ApexPost branch
  I want to register a parcel under its tracking number
  So that couriers can scan it and the customer can track it

  These scenarios run against the API with a real database (SQLite in memory) and a test JWT,
  because the weight limit and the duplicate check are rules the caller sees at the API.

  Background:
    Given I am signed in as an administrator
    And the parcel is handed in at branch "B001", which is open

  Scenario: A valid shipment is registered
    When I register shipment "APX-40000001" weighing 2.5 kg for "Mia Fictional" in "Sydney"
    Then the shipment is registered
    And its status is "Created"
    And it can be found at the address the API returned

  Scenario: A tracking number can be used only once
    Given shipment "APX-40000001" is already registered
    When I register shipment "APX-40000001" weighing 1 kg for "Noah Example" in "Perth"
    Then the shipment is refused because the tracking number is taken
    And there is still only one shipment "APX-40000001"

  Scenario Outline: A parcel must weigh from 0.1 kg to 70 kg
    When I register shipment "APX-40000002" weighing <weight> kg for "Mia Fictional" in "Sydney"
    Then the shipment is <outcome>

    Examples: At and inside the limits
      | weight | outcome    |
      | 0.1    | registered |
      | 35     | registered |
      | 70     | registered |

    Examples: Outside the limits
      | weight | outcome                   |
      | 0      | refused for its weight    |
      | 0.09   | refused for its weight    |
      | 70.01  | refused for its weight    |
