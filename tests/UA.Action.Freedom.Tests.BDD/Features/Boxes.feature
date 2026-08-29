Feature: Boxes API
    The deployed Freedom service exposes packed boxes at /boxes and their contents at
    /boxes/{id}/items. Reads are open to every operational role; packing is Administrator,
    Dispatcher and Loader; validating is Administrator and Loader.

    Validation is the moment that matters. A Loader opens the box, checks what is inside
    and weighs it — that is the trust boundary between the donor and Ukrainian Action, and
    the confirmed weight is what a border check relies on. It happens once, and afterwards
    the box is frozen: nothing in, nothing out, no new receiver.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/boxes"

Scenario: Reading boxes requires a token
    When I GET "/boxes" without a token
    Then the response status is 401

Scenario: A ground officer is refused the cargo list
    Given I am authenticated as "groundofficer"
    When I GET "/boxes"
    Then the response status is 403

Scenario: A loader packs a box, which starts with no confirmed weight
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "house": "Unit 4", "street": "Cross Road", "city": "Coventry", "country": "United Kingdom", "postcode": "CV1 2AB" }
        """
    Then the response status is 201
    When I GET "/boxes/{id}"
    Then the response status is 200
    And the response body field "weightKg" is "0"
    And the response body field "validated" is "False"

Scenario: Items packed into an open box keep their open-ended properties
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry", "postcode": "CV1 2AB" }
        """
    Then the response status is 201
    When I POST "/boxes/{id}/items" with body:
        """
        { "description": "Blankets", "properties": { "size": "double", "condition": "new" } }
        """
    Then the response status is 204
    When I GET "/boxes/{id}/items"
    Then the response status is 200
    And the response body is a list of 1 or more

Scenario: An item with no description is rejected
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    When I POST "/boxes/{id}/items" with body:
        """
        { "description": "" }
        """
    Then the response status is 400
    And the response body names "Description" as invalid

Scenario: A box with no contents yet has an empty list, not a missing one
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    When I GET "/boxes/{id}/items"
    Then the response status is 200
    And the response body is a list of 0 or more

Scenario: A loader validates a box and the weight becomes authoritative
    Given I am authenticated as "admin"
    And a volunteer exists who can validate boxes
    When I POST "/boxes" with body:
        """
        { "city": "Coventry", "postcode": "CV1 2AB" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/validate" on the remembered box with the validating volunteer weighing 24
    Then the response status is 204
    When I GET "/boxes/{id}" on the remembered box
    Then the response body field "validated" is "True"
    And the response body field "weightKg" is "24"

Scenario: A validated box will not take another item
    Given I am authenticated as "admin"
    And a volunteer exists who can validate boxes
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/validate" on the remembered box with the validating volunteer weighing 18
    Then the response status is 204
    When I POST "/boxes/{id}/items" on the remembered box with body:
        """
        { "description": "Blankets" }
        """
    Then the response status is 409

Scenario: A box cannot be validated twice
    Given I am authenticated as "admin"
    And a volunteer exists who can validate boxes
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/validate" on the remembered box with the validating volunteer weighing 18
    Then the response status is 204
    When I POST "/boxes/{id}/validate" on the remembered box with the validating volunteer weighing 25
    Then the response status is 409
    When I GET "/boxes/{id}" on the remembered box
    Then the response body field "weightKg" is "18"

Scenario: A validated box cannot be moved or re-pointed
    Given I am authenticated as "admin"
    And a volunteer exists who can validate boxes
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/validate" on the remembered box with the validating volunteer weighing 18
    Then the response status is 204
    When I PUT "/boxes/{id}" on the remembered box with body:
        """
        { "city": "Lviv", "country": "Ukraine" }
        """
    Then the response status is 409

Scenario: Naming a validator who is not a volunteer on file is refused
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    When I POST "/boxes/{id}/validate" with body:
        """
        { "validatedByPersonId": "6f9619ff-8b86-d011-b42d-00cf4fc964ff", "weightKg": 20 }
        """
    Then the response status is 404

Scenario: A box validated at an implausible weight is rejected
    Given I am authenticated as "operator"
    When I POST "/boxes" with body:
        """
        { "city": "Coventry" }
        """
    Then the response status is 201
    When I POST "/boxes/{id}/validate" with body:
        """
        { "validatedByPersonId": "6f9619ff-8b86-d011-b42d-00cf4fc964ff", "weightKg": 9999 }
        """
    Then the response status is 400

Scenario: Fetching an unknown box is a 404
    Given I am authenticated as "operator"
    When I GET "/boxes/99999999"
    Then the response status is 404
