Feature: Declarations go stale when the load changes
    A declaration stores the load it was written from and is stale whenever the load now differs
    (ADR 0005). Nobody sets the flag: changing the vehicle's cargo is enough, the declaration then
    reads Stale, and an immediate re-declare task appears for the Dispatcher. Withdrawing the stale
    declaration keeps it and its reference as history and starts a new draft, and recording a fresh
    reference clears the task.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/boxes"

Scenario: Changing the cargo of a vehicle makes its filed GMR stale and raises a task until it is re-declared
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    When I POST "/boxes" with body:
        """
        {}
        """
    Then the response status is 201
    Given I remember the box
    When I put the remembered box on the insured vehicle
    Then the response status is 204
    When I POST "/gmr/ready" on the remembered vehicle's declarations
    Then the response status is 204
    When I POST "/gmr/record" on the remembered vehicle's declarations with body:
        """
        { "reference": "BDD-GMR-1" }
        """
    Then the response status is 204
    And the "Gmr" declaration of the insured vehicle reads "Filed"
    And the convoy has 0 re-declare tasks

    When I take the remembered box off the insured vehicle
    Then the response status is 204
    And the "Gmr" declaration of the insured vehicle reads "Stale"
    And the convoy has 1 re-declare tasks

    When I withdraw the stale "Gmr" declaration of the insured vehicle
    Then the response status is 204
    And the "Gmr" declaration of the insured vehicle reads "Draft"
    And the convoy has 0 re-declare tasks

    When I POST "/gmr/record" on the remembered vehicle's declarations with body:
        """
        { "reference": "BDD-GMR-2" }
        """
    Then the response status is 204
    And the "Gmr" declaration of the insured vehicle reads "Filed"
    And the convoy has 0 re-declare tasks

Scenario: A declaration that still matches its load cannot be withdrawn
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    When I POST "/gmr/record" on the remembered vehicle's declarations with body:
        """
        { "reference": "BDD-GMR-3" }
        """
    Then the response status is 204
    When I withdraw the "Gmr" declaration of the insured vehicle
    Then the response status is 409

Scenario: A ground officer is refused the re-declare tasks
    Given I am authenticated as "groundofficer"
    And the Freedom API exposes "/convoys"
    When I GET "/convoys/1/tasks"
    Then the response status is 403
